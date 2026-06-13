# Use Case: Resource Regeneration & Rest

**Status:** implemented
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
- **Rest breaks on action.** Two mechanisms ensure rest does not persist through activity:
  - **Movement** — the existing movement command exits `Resting` before moving (guard + "You stop resting and stand up." line + `EntityStateChangedEvent`). Movement does not call `TryEnterState`, so the auto-exit rule below does not fire; this explicit hook is required.
  - **Combat entry** — entering `InCombat` clears `Resting` via the `EntityStateService` auto-exit rule (see [entity-state-management.md](../features/combat/combat.md) rule table amendment). `TryEnterState(InCombat, …)` applies `ExitState(Resting)` before setting the flag, regardless of which call site initiates combat. No explicit `ExitState(Resting)` call is needed in `KillCommand`, `AbilityInvocationPipeline`, or any future combat-initiating path — the invariant is centrally enforced.

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

## Design notes

- **State-based rate, global cadence — no per-entity timer.** Using `tickId % IdleIntervalTicks` for the idle cadence (and every-tick for resting) means regeneration needs **no new component** and **no per-entity accumulator** — all idle entities recover on the same tick. This is the minimal correct shape; a later per-entity/stat-derived model (the dedicated regen UC) can add state if needed.
- **Rates are Category-3, hardcoded by deliberate deferral.** Per the owner decision, the amount/cadence/multiplier are hardcoded now; surfacing them as configuration depends on the robust-config model (backlog), so the *configurability* is deferred to a dedicated regeneration use-case rather than bolted on here. The constants are isolated in `RegenerationSystem` so the promotion is a cheap, localized change.
- **Resting is a regen modifier, not a gate.** `Resting` does not block commands; it only multiplies regeneration and is broken by movement/combat. This keeps the slice minimal (no command-gating table changes) while giving 9-a's `Resting` flag its first real consumer.
- **`Resting` is cleared on combat entry via the auto-exit rule, not at call sites.** The base 9-a `TryEnterState` OR-assigns only. This slice amends `EntityStateService` with a static `_autoExits` table: `InCombat → Resting`. After validation passes, `TryEnterState` calls `ExitState(Resting)` before setting the new flag, regardless of which initiator enters combat. The calling command captures `oldStates` before the call and publishes a single `EntityStateChangedEvent` with the full before/after — no extra event is needed for the auto-exit. Movement still requires an explicit hook (movement does not call `TryEnterState`).
- **Mobs regenerate too.** "Every entity not in combat" includes mobs: a damaged mob that combat left alive (e.g. the player fled) slowly heals out of combat. This is intended MUD behavior and falls out of the `PoolsComponent` sweep for free; mobs never `rest` (no command), so they only ever get the idle rate.
- **Silent by design.** Per-tick regeneration produces **no** message (a "you regenerate 1 hp" line every few seconds would be spam); the player observes recovery via `score`. A future "fully rested" notification, if wanted, is an additive handler — out of scope.

---

## Related

- [`stat-resource-substrate.md`](stat-resource-substrate.md) — **9-d**; `PoolsComponent`, `ResourceType`, the `IAttributeSystem` clamped pool setters regeneration writes through.
- [`time-system.md`](time-system.md) — **9-b**; `HeartbeatTickEvent` (`TickId`) drives the regeneration sweep.
- [`entity-state-management.md`](../features/combat/combat.md) — **9-a**; the `Resting` flag, `IEntityStateService`, `EntityStateChangedEvent`, and the `Resting`↔`InCombat` transition rule this slice is the first to consume.
- [`combat.md`](../features/combat/combat.md) — **9**; `InCombat` suppresses regeneration; the combat-entry break hook; the periodic-flush precedent for pool mutations.
- [`ability-substrate.md`](ability-substrate.md) / [`ability-invocation.md`](ability-invocation.md) — **11-a/11-b**; the ability resource costs this slice makes recoverable (the motivating consumer, though regeneration does not depend on them).
- [`../design/gameplay-model.md`](../design/gameplay-model.md) — §3 Substrate (pools).

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
