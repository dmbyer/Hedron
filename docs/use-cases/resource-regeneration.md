# Use Case: Resource Regeneration & Rest

**Status:** planned
**Actors:** Player, Mob, System
**Module:** `Core/Modules/Regeneration/` (new — `IRegenerationSystem`, `RegenerationTickHandler`, `rest`/`stand` commands, `RegenerationModule`); `Core/Modules/EntityState/` (reads `Resting` flag, slice 9-a); `Core/Modules/Attributes/` (pool writes via `IAttributeSystem`); `Core/Modules/Time/` (heartbeat consumer)

**Spine:** not a gameplay-model spine — a **supporting pool mechanic** over the S1 substrate that makes S4 (abilities) sustainable: without regeneration, Stamina/Mana/Astra drain to zero and never recover, so ability costs become a one-time budget. This is sub-slice **11-c** of cluster 11; it is **independent of 11-a/11-b** (it needs only pools + heartbeat + the entity-state flag) and may land in any order relative to them. See [`../design/gameplay-model.md`](../design/gameplay-model.md) §3 Substrate (pools).

---

## Description

Add **out-of-combat resource regeneration** and a **`rest`** state that accelerates it. Every entity **not** in combat slowly regenerates each pool (HP, Mana, Stamina, Astra); an entity that is **`Resting`** (slice 9-a's flag, entered via `rest`) regenerates faster. Combat suppresses regeneration entirely. The heartbeat drives it: a `RegenerationTickHandler` calls `IRegenerationSystem`, which reads each entity's state and applies the per-pool deltas through `IAttributeSystem`'s clamped setters.

This is a deliberately **small, hardcoded-rate** slice. The rates (amount per tick, the idle cadence, the resting multiplier) are **Category-3 balance constants** baked into the system — *not* configuration this slice, because surfacing them properly depends on the backlogged robust-config model. A later **dedicated regeneration use-case** promotes the rates to configuration/content and adds the richer model (per-area rates, stat-derived regen, food/effects interaction); this slice ships the baseline only.

The mechanic is a **closed sweep with no downstream chain**: regeneration publishes no event (INV-10's no-chain shape — nothing reacts to a tick of regen). The only events are the existing `EntityStateChangedEvent` (slice 9-a) the `rest`/`stand` commands already publish on a state transition.

---

## Preconditions

- Slice **9-d (stat & resource substrate)** complete: `PoolsComponent` with four pools (HP/Mana/Stamina/Astra, current + max); `ResourceType`; `IAttributeSystem` clamped pool setters (`SetCurrentHp`/`SetCurrentMana`/`SetCurrentStamina`/`SetCurrentAstra`, each `[0, max]`-clamped) and the matching getters.
- Slice **9-b (heartbeat)** complete: `IHeartbeatService` / `HeartbeatTickEvent { long TickId, DateTimeOffset Timestamp, TimeSpan Elapsed }`. (`TickId` drives the every-Nth-tick cadence.)
- Slice **9-a (entity state)** complete: `EntityStateFlags` (`None | InCombat | Resting | Incapacitated`); `IEntityStateService` (`TryEnterState`, `ExitState`, `IsInState`, `GetStates`); `EntityStateChangedEvent` published by callers on a transition; the static transition-rule table (which governs `Resting` ↔ `InCombat` exclusivity — see Design notes).
- Reused (no change): `EntityService`, `IEventBus`, command framework + `IOutputWriter`/`PlainMessage`, `HandlerPriority`. **No persistence change** — pools are already `[Persistent]` (covered by periodic flush); the `Resting` flag is transient (slice 9-a).

---

## Postconditions (requirements)

**Regeneration system (`IRegenerationSystem` — DOMAIN, `Core/Modules/Regeneration/Systems/`)**
- `ApplyTickRegen(long tickId) → void` — the per-tick sweep. Iterates every entity with a `PoolsComponent` (`EntityService.GetAllComponents<PoolsComponent>()`); for each, reads its state via `IEntityStateService` and applies the per-pool regeneration for this tick through `IAttributeSystem`'s clamped setters. **Returns nothing, never publishes events, never persists (INV-5).** Domain → domain (`IEntityStateService`) and domain → domain (`IAttributeSystem`) calls are legal.
- **State-based rate (the rule, all Category-3 constants in the system):**
  - **`InCombat`** → **no regeneration** (suppressed entirely; combat is not a time to recover).
  - **`Resting`** → `+RegenAmount` to each pool **every tick** (the accelerated rate).
  - **Idle** (neither `InCombat` nor `Resting`) → `+RegenAmount` to each pool **every `IdleIntervalTicks`-th tick** (`tickId % IdleIntervalTicks == 0`).
  - `Incapacitated` is treated as not-resting/not-in-combat for regeneration (idle rate) unless a later slice says otherwise — flagged, not load-bearing.
  - Defaults: `RegenAmount = 1`, `IdleIntervalTicks = 3` (so idle = 1/pool/3 ticks, resting = 1/pool/tick ≈ 3× idle). A global `tickId`-modulo cadence means all idle entities regenerate on the same tick — **no per-entity timer/component is needed**.
- Every applied delta rides the existing `[0, max]` clamp in `IAttributeSystem` (INV-8): a pool at max is a no-op; no pool ever exceeds its max.

**Heartbeat tick handler**
- `RegenerationTickHandler` subscribes to `HeartbeatTickEvent` (priority `HandlerPriority.Domain` = 20, alongside the effect/combat/cooldown tick handlers) and calls `IRegenerationSystem.ApplyTickRegen(@event.TickId)`. **It publishes nothing** — regeneration is a closed mechanical sweep with no downstream concern (INV-10's no-chain variant, exactly like the 11-a cooldown tick and the persistence flush). Orchestration only (INV-1).

**`rest` / `stand` commands (player surface)**
- `rest` (no privilege, `Partial`) — calls `IEntityStateService.TryEnterState(invokerEntityId, Resting, out failReason)`. On success: writes "You sit down and begin to rest." and publishes `EntityStateChangedEvent` (the caller publishes, per the 9-a pattern). On failure (e.g. `InCombat` per the 9-a rule table): writes `failReason` ("You can't rest while fighting!").
- `stand` (alias `wake`, no privilege, `Partial`) — calls `IEntityStateService.ExitState(invokerEntityId, Resting)`; writes "You stand up." and publishes `EntityStateChangedEvent`. A no-op (already standing) writes "You are already standing."
- **Rest breaks on action.** Two minimal hooks ensure rest does not persist through activity:
  - **Movement** — the existing movement command exits `Resting` before moving (a one-line `if (IsInState(Resting)) ExitState(Resting)` guard with a "You stop resting and stand up." line). 
  - **Combat entry** — entering `InCombat` clears `Resting`. **Verified against slice 9-a:** `TryEnterState(InCombat)` is blocked only by `Incapacitated` and merely OR-assigns the flag — it neither blocks on nor auto-clears `Resting`. So the combat-initiation path (`KillCommand`, and the 11-b offensive-ability opener if present) **must** call `ExitState(Resting)` when entering combat, otherwise an entity ends up flagged both `Resting` and `InCombat`. This explicit hook is **required** (not a "if 9-a doesn't auto-clear" fallback).

**Inspection & authoring (INV-18)**
- **Inspection** is the existing `score` command (slice 9-d): a player watches pools climb over time, faster while resting. No new inspection surface is needed — regeneration's observable state *is* the pool values `score` already renders.
- **Authoring** of the rates is **deferred**: they are hardcoded Category-3 constants this slice (matching how `EffectRegistry`/`AbilityRegistry` hardcode balance). Promotion to configuration/content lands with the dedicated regeneration use-case (which the robust-config model gates). No admin command to set regen rates ships here.

**Events**
- **No new event types.** Regeneration publishes nothing (no-chain sweep). `rest`/`stand` publish the existing `EntityStateChangedEvent` (slice 9-a), exactly as other state-changing callers do.

---

## Main flow

### Flow 1 — Idle regeneration (out of combat, not resting)
1. `HeartbeatTickEvent` fires (carries `TickId`). `RegenerationTickHandler` (priority 20) calls `IRegenerationSystem.ApplyTickRegen(tickId)`.
2. For a player standing idle in a room (state `None`), on every `IdleIntervalTicks`-th tick (`tickId % 3 == 0`) the system adds `+1` to each of HP/Mana/Stamina/Astra via `IAttributeSystem` (clamped at max).
3. The player's next `score` shows the pools slightly higher. No message, no event (silent recovery).

### Flow 2 — `rest` accelerates regeneration
1. Player types `rest`. `RestCommand` calls `IEntityStateService.TryEnterState(playerId, Resting)` → success (not in combat). Writes "You sit down and begin to rest."; publishes `EntityStateChangedEvent`.
2. On **every** subsequent tick, `ApplyTickRegen` sees the player `IsInState(Resting)` and adds `+1` to each pool (the accelerated rate — 3× idle).
3. Player types `stand` → `ExitState(Resting)` → "You stand up."; `EntityStateChangedEvent`. Regeneration reverts to the idle cadence.

### Flow 3 — Combat suppresses regeneration
1. A player in combat (`InCombat`) receives no regeneration: `ApplyTickRegen` skips `InCombat` entities entirely. HP/Mana/Stamina/Astra only change via combat/ability mechanics during the fight.
2. On `flee`/combat end, the player returns to idle (or may `rest`) and regeneration resumes.

### Flow 4 — Rest breaks on action
1. A resting player types `north`. The movement command sees `IsInState(Resting)`, calls `ExitState(Resting)` ("You stop resting and stand up."), then performs the move. The player is now idle (slow regen).
2. A resting player is drawn into combat (`kill`, or an incoming attack in a later slice): entering `InCombat` clears `Resting` (per 9-a rules / the combat-entry hook). Regeneration is then suppressed (Flow 3).

---

## Events fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `EntityStateChangedEvent` (existing, 9-a) | `RestCommand`, `StandCommand` (and the movement/combat-entry hooks that clear `Resting`) | `uint EntityId, EntityStateFlags OldStates, EntityStateFlags NewStates` (9-a field names) | state-transition observers (existing) |

`IRegenerationSystem` and `IAttributeSystem` **never publish** (INV-5). `RegenerationTickHandler` publishes nothing (no-chain sweep, INV-10). **No new event types are introduced.**

---

## Implementation plan — work packages

> **Sub-agent execution.** Self-contained packages for a limited-context model. **WP-1 lands first** (the system + tick handler — the regeneration substrate). **WP-2** (the `rest`/`stand` commands + the action-break hooks) depends only on slice 9-a and is independent of WP-1's internals, so the two may run in parallel. The **primary agent runs `architecture-reviewer` (code mode)** across the combined diff once both land. Mirror the WP discipline of [`stat-resource-substrate.md`](stat-resource-substrate.md).

### WP-1 — Regeneration system + tick handler *(no command surface)*
- **Scope:** the state-based per-tick regeneration and its heartbeat driver. Nothing a player types.
- **Files:**
  - `Core/Modules/Regeneration/Systems/IRegenerationSystem.cs` + `RegenerationSystem.cs` — `ApplyTickRegen(long tickId)`; reads `PoolsComponent` (via `EntityService.GetAllComponents`) + `IEntityStateService`; applies per-pool deltas via `IAttributeSystem` clamped setters per the state-based rate; the `RegenAmount`/`IdleIntervalTicks` constants live here. Pure of events/persistence (INV-5).
  - `Core/Modules/Regeneration/Handlers/RegenerationTickHandler.cs` — subscribes `HeartbeatTickEvent` (priority 20); calls `ApplyTickRegen(@event.TickId)`; publishes nothing.
  - `Core/Modules/Regeneration/RegenerationModule.cs` — `AddRegenerationModule(IServiceCollection)` registers the system + the tick handler subscription. Call from `Server/Program.cs`. (Verify the `RegenerationSystem` type name does not collide with the `Regeneration` module namespace per the CLAUDE.md rule — it does not, the simple names differ.)
- **Depends on:** 9-d (pools/setters), 9-b (heartbeat), 9-a (state). Lands first.
- **Out of scope:** the `rest`/`stand` commands; the movement/combat break hooks (WP-2).
- **Exit (testable):** solution builds; an idle entity gains `+1` to each pool every 3rd tick (clamped at max); a `Resting` entity gains `+1` every tick; an `InCombat` entity gains nothing; no pool exceeds its max; no event is published by the tick path.

### WP-2 — `rest`/`stand` commands + action-break hooks *(depends on 9-a)*
- **Scope:** the player state surface and the two minimal hooks that break rest on action; the reference-catalog/flows sweep for the whole slice.
- **Files:**
  - `Core/Modules/Regeneration/Commands/RestCommand.cs` — `rest`; `TryEnterState(Resting)` → message + `EntityStateChangedEvent` (or the `failReason`). Register in `RegenerationModule`.
  - `Core/Modules/Regeneration/Commands/StandCommand.cs` — `stand` (alias `wake`); `ExitState(Resting)` → message + `EntityStateChangedEvent`.
  - **Movement hook** — the existing movement command: before moving, `if (IsInState(Resting)) { ExitState(Resting); /* "You stop resting and stand up." */ }`. **Movement behavior is otherwise unchanged.** Note: `MoveCommand` currently has **no** `IEntityStateService` dependency, so this adds one constructor dependency + the guard + an output line — purely additive, but more than a literal one-liner.
  - **Combat-entry hook** — confirm slice-9-a's `Resting`↔`InCombat` rule. If entering `InCombat` does not auto-clear `Resting`, add `ExitState(Resting)` to the combat-initiation path (`KillCommand`; the 11-b offensive-ability opener inherits the same guard). **Combat behavior is otherwise unchanged.**
  - **Catalog + flows sweep (WP-2 owns it for the whole slice):**
    - `docs/reference/commands.md` — add `rest`, `stand`/`wake`.
    - `docs/reference/systems.md` — add `RegenerationSystem` (domain).
    - `docs/reference/handlers.md` — add `RegenerationTickHandler`.
    - `docs/architecture/flows/README.md` — modify Flow 16 (heartbeat tick) to list the new subscriber; no new standalone flow (regeneration is a no-chain sweep — see Flows).
- **Depends on:** 9-a (state service). Independent of WP-1's internals.
- **Exit (testable):** `rest` enters `Resting` (blocked with a message while in combat) and accelerates regen; `stand`/`wake` exits; moving while resting auto-stands and reverts to idle regen; entering combat clears `Resting`; catalogs/flows match the code.

---

## Content tooling impact

- **Observable state ships its inspection in-slice (INV-18):** regeneration's only player-visible state is the pool values, already inspected by `score` (slice 9-d) — pools climbing over time, faster while resting, is the inspection. The `rest`/`stand` commands are the player authoring of the `Resting` state. No new inspection surface is required.
- **Rate authoring is deferred, explicitly:** the regen amount, idle cadence, and resting multiplier are hardcoded Category-3 constants (same posture as `EffectRegistry`/`AbilityRegistry` balance data). Promotion to configuration/content — and the richer model (per-area/terrain rates, stat-derived regen, food/effect interaction) — is the **dedicated regeneration use-case**, which depends on the backlogged robust-config model. This slice deliberately ships only the flat baseline so the ability/combat loop is sustainable now; it is tracked as a backlog item.

---

## Cross-cutting surfaces stressed

- **Time / heartbeat — Adequate.** One new `HeartbeatTickEvent` subscriber at priority 20, alongside the effect/combat/cooldown tick handlers. No change to the heartbeat itself; the per-tick fan-out already supports independent priority-20 domain handlers. The `TickId`-modulo cadence needs no scheduler change. See Flows (Flow 16 gains a subscriber).
- **Commands — Adequate.** `rest`/`stand` are ordinary `Partial` player commands on the existing framework; no new command infrastructure. Output via `PlainMessage` (INV-11).
- **Entity state — Adequate (reuses 9-a).** `Resting` is slice 9-a's flag; this slice is its first behavioral *consumer* (9-a shipped the flag and rules; nothing read `Resting` until now). The `rest`/`stand` commands and the break hooks use `IEntityStateService` exactly as combat uses it for `InCombat`. The `Resting`↔`InCombat` exclusivity is a 9-a rule, not new logic.
- **ECS queries — Adequate.** `ApplyTickRegen` uses `EntityService.GetAllComponents<PoolsComponent>()` + `IEntityStateService` reads — the established query seam (mirrors `EffectSystem.AdvanceTick` and the 11-a cooldown tick). No `is`/`as` (INV-4).
- **Persistence — Adequate (no change).** Pools are already `[Persistent]` (slice 8a/9-d) and covered by the periodic flush — regeneration mutations ride that flush exactly like combat HP changes (combat.md: "save-on-change is not used for combat HP"). **No `SaveEntityAsync` is added** (INV-22): the tick handler and the `rest`/`stand` commands make no persistence call (the `Resting` flag is transient by design). No `PersistentEntity` change; no component domain change (INV-23).
- **Event bus — Adequate.** **No new events.** Regeneration is a no-chain sweep (INV-10); `rest`/`stand` reuse `EntityStateChangedEvent` (9-a). 
- **Configuration — Acknowledged deferral.** The rates are balance constants that *should* eventually be configurable, but are hardcoded here pending the robust-config model (backlog). This is a conscious, documented deferral (not an oversight) — the dedicated regeneration use-case owns the promotion. Backlog entry required (INV-19 acknowledged-debt).
- **Modules — Adequate.** New `Core/Modules/Regeneration/` with `AddRegenerationModule`, called from `Server/Program.cs` (standard feature-module composition; no `IModule` interface).

---

## Flows introduced or modified

- **Modified — Flow 16 (heartbeat tick).** Add `RegenerationTickHandler` to the list of priority-20 `HeartbeatTickEvent` subscribers (alongside the effect, combat, and 11-a cooldown tick handlers). Regeneration is a **no-chain sweep** — a single `ApplyTickRegen` call with no event fan-out — so it does **not** warrant its own canonical flow (the no-chain shape, like the persistence flush). Flow 16's body gains a one-line subscriber note; no diagram change beyond listing it.
- **No new canonical flow.** `rest`/`stand` are single-step state transitions publishing the existing `EntityStateChangedEvent`; they do not introduce a multi-step runtime chain worth a `flows/` entry.

---

## Design notes

- **State-based rate, global cadence — no per-entity timer.** Using `tickId % IdleIntervalTicks` for the idle cadence (and every-tick for resting) means regeneration needs **no new component** and **no per-entity accumulator** — all idle entities recover on the same tick. This is the minimal correct shape; a later per-entity/stat-derived model (the dedicated regen UC) can add state if needed.
- **Rates are Category-3, hardcoded by deliberate deferral.** Per the owner decision, the amount/cadence/multiplier are hardcoded now; surfacing them as configuration depends on the robust-config model (backlog), so the *configurability* is deferred to a dedicated regeneration use-case rather than bolted on here. The constants are isolated in `RegenerationSystem` so the promotion is a cheap, localized change.
- **Resting is a regen modifier, not a gate.** `Resting` does not block commands; it only multiplies regeneration and is broken by movement/combat. This keeps the slice minimal (no command-gating table changes) while giving 9-a's `Resting` flag its first real consumer.
- **`Resting` is cleared explicitly on combat entry (9-a does not do it).** Verified against slice 9-a: entering `InCombat` is not blocked by `Resting` and does not auto-clear it (`TryEnterState` OR-assigns only). So this slice's combat-initiation hook **must** call `ExitState(Resting)` — it does not rely on a 9-a auto-clear. This slice adds no new *state rule*; it adds the explicit clear at the combat-entry call site (and the movement call site).
- **Mobs regenerate too.** "Every entity not in combat" includes mobs: a damaged mob that combat left alive (e.g. the player fled) slowly heals out of combat. This is intended MUD behavior and falls out of the `PoolsComponent` sweep for free; mobs never `rest` (no command), so they only ever get the idle rate.
- **Silent by design.** Per-tick regeneration produces **no** message (a "you regenerate 1 hp" line every few seconds would be spam); the player observes recovery via `score`. A future "fully rested" notification, if wanted, is an additive handler — out of scope.
- **On ship:** fold the regeneration model into `architecture/subsystems/` (a short vitals/regeneration note, or the stats subsystem doc) and trim this doc to the durable behavior spec (docs lifecycle; INV-D2).

---

## Resolved planning inputs

Settled with the owner (the ratified 11-c scope):
1. **Baseline regeneration applies to all entities not in combat** — `+1` per pool per `IdleIntervalTicks` (3) ticks; combat suppresses it.
2. **`rest` accelerates regeneration** (every tick — ~3× idle) via the 9-a `Resting` flag; `stand`/`wake` exits.
3. **Rates are hardcoded Category-3 constants** this slice; configurability + the richer model are deferred to a **dedicated regeneration use-case** that depends on the backlogged robust-config model.
4. **Independent of 11-a/11-b** — needs only pools (9-d), heartbeat (9-b), and the entity-state flag (9-a); may land in any order within cluster 11.

---

## Open questions

1. **(Confirm, non-blocking) Resting acceleration magnitude.** Resting = every-tick regen (≈ 3× the idle 1/3-tick rate). Is a 3× resting multiplier the intended feel, or stronger (e.g. a larger amount per tick)? Numbers are tunable Category-3 constants; the *shape* (resting = faster) is decided. *Non-blocking.*
2. **(Resolved against 9-a) Combat-entry vs `Resting`.** Verified: slice 9-a's `TryEnterState(InCombat)` is blocked only by `Incapacitated` and only OR-assigns the entered flag — it neither blocks on nor clears `Resting`. So the combat-initiation path **must** explicitly `ExitState(Resting)` (WP-2's combat-entry hook); there is no auto-clear to rely on. **No remaining fork.**

---

## Related

- [`stat-resource-substrate.md`](stat-resource-substrate.md) — **9-d**; `PoolsComponent`, `ResourceType`, the `IAttributeSystem` clamped pool setters regeneration writes through. **WP structure mirrored from here.**
- [`time-system.md`](time-system.md) — **9-b**; `HeartbeatTickEvent` (`TickId`) drives the regeneration sweep.
- [`entity-state-management.md`](entity-state-management.md) — **9-a**; the `Resting` flag, `IEntityStateService`, `EntityStateChangedEvent`, and the `Resting`↔`InCombat` transition rule this slice is the first to consume.
- [`combat.md`](combat.md) — **9**; `InCombat` suppresses regeneration; the combat-entry break hook; the periodic-flush precedent for pool mutations.
- [`ability-substrate.md`](ability-substrate.md) / [`ability-invocation.md`](ability-invocation.md) — **11-a/11-b**; the ability resource costs this slice makes recoverable (the motivating consumer, though regeneration does not depend on them).
- [`../design/gameplay-model.md`](../design/gameplay-model.md) — §3 Substrate (pools).

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
