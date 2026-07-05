# Ascension (character-wide tier) — slice prog-2

**Status:** planned
**Actors:** Player (ascends; sees a rebalanced world at the new tier) · Administrator/Designer (triggers `ascend` on a fixture; authors tier-band tags on mobs) · System (folds the tier baseline on every stat read)
**Module:** `Core/Modules/Ascension/` (new) — `AscensionComponent`, `IAscensionSystem`, `AscensionEffectContributor`, `AscensionConstants`, `AscendedEvent`, `AscendCommand`; extends `Core/Modules/Mobs/` (tier-band content tag); reads `IStatSystem`/`IEffectSystem` (the existing `IEffectContributor` port). Feature home on ship: [`../features/progression/`](../features/progression/).

> **Slice 2 of the five-slice Progression & Balance program.** The durable cross-slice seams and rationale live in the program brief [`progression-and-balance.md`](progression-and-balance.md) (`## Architecture brief`, `Resolved decisions`, `Family disposition`, `Slice 2` row) — this plan **extends** that seed and does not relitigate its decisions. Slice 1 (progression substrate) shipped ([`../roadmap/completed/progression-substrate.md`](../roadmap/completed/progression-substrate.md)); this slice mirrors its shapes exactly. Slice 3 (`IPowerBudgetSystem` + banding math) builds on this.

> **Owner-resolved decisions (prog-2 planning, 2026-07-05) — all closed; no open questions remain.**
> 1. **Power model = additive baseline, no reset** (brief open-Q#1). Tier confers a flat additive power baseline; no XP rescale/reset.
> 2. **New `Core/Modules/Ascension/` module** — `AscensionComponent` (tier scalar + unlock flags) + `IAscensionSystem`; not folded into Progression (matches R1).
> 3. **Tier baseline rides the existing `IEffectContributor` port** (brief open-Q#5) — a 4th registrant; no new `IScalingSystem`/Spine-D seam.
> 4. **Tier-up gate = admin `ascend` + `IAscensionSystem.CanAscend`/`TryAscend`** — real player-facing Ascension-Objective gate deferred (`IObjectiveSystem` unbuilt).
> 5. **Band tagging = mobs only** — item bands deferred to slice 3 (their consumer, the power-budget oracle, lands there; `ItemDataComponent` is `[Persistent]`, so an item-band field is a slice-3 concern).
> 6. **Unlocks = seam only** — ship `AscensionComponent.GrantedUnlocks` + `GetGrantedUnlocks` + `AscendedEvent` with an **empty** unlock table; the grant-execution seam (`GrantFlag`/`GrantAbility` are unimplemented `EffectKind` values) and concrete unlock content are deferred. `TierBand` is a plain `int` `0–6` (range-validated in `setmob band`), not an enum.

---

## Description

A character-wide **Tier** scalar (Ascension, gameplay-model [R1](../design/gameplay-model.md#6-resolved-decisions)) — an `int` `0–6` on a new `[Persistent]`, player-only `AscensionComponent` — confers a flat **additive power baseline** across every tracked score, folded on read into `IStatSystem.Get` through a fourth `IEffectContributor` registrant (`AscensionEffectContributor`), the same contribute-on-read seam slice 1 proved (INV-24) — never stored. A maxed lower tier keeps all its per-track progression power; ascending *layers the baseline on top* (there is **no XP reset or rescale** — the additive baseline does the "fresh climb" work). This makes the deadly→medium overlap semantics fall out: a Tier-1 character facing a Tier-2-tuned mob is out-scaled (deadly); ascending normalizes that same mob to medium. Content carries a **lightweight tier-band tag** (`TierBand` on `MobTemplate`, authored + inspectable) with mechanical threat **emergent** from the baseline — a Tier-N mob is simply tuned to Tier-N baseline stats; bands **overlap** (a maxed lower tier can reach into the next band before formally ascending). Tier-up runs through `IAscensionSystem.CanAscend`/`TryAscend`; the **only trigger now is an admin `ascend` command** (plain verb, structural privilege gate) — the real player-facing **Ascension-Objective** gate is deferred (`IObjectiveSystem` is unbuilt) and will wire into `CanAscend` later. Ascending publishes `AscendedEvent`; prog-2 ships only the **unlock-record seam** (`AscensionComponent.GrantedUnlocks` + `GetGrantedUnlocks`, an **empty** unlock table) that a future grant handler will consume — the grant-execution seam and the selection UX are both deferred.

---

## Preconditions

- The progression substrate (slice 1) is live: `ProgressionComponent`, `IProgressionSystem`, `ProgressionEffectContributor` registered on the `IEffectContributor` port, `IStatSystem.Get(entity, ScoreId)` folding all contributors.
- The target entity is a persistent player entity (carries `PersistentEntity`, `AttributesComponent`, `PoolsComponent`).
- The invoker of `ascend` is a privileged session (`AdminRequirement` — the structural gate; no sigil, per owner decision).
- `AscensionComponent`, `IAscensionSystem`, `IObjectiveSystem`, `IScalingSystem`, `AscendedEvent`, and any `ascend` command are **greenfield** — none exist in code (verified).
- **No grant-execution seam exists:** `GrantFlag`/`GrantAbility` are unimplemented `EffectKind` enum values (`Core/Modules/Effects/Effect.cs`), not a callable "grant X to entity" path (verified). prog-2 ships the unlock-*record* seam only.

---

## Postconditions

> The coverage contract. Every item asserting player-invisible internal state maps to a named test in the Test plan.

- **Tier default & bounds.** An entity with no `AscensionComponent` reads as Tier 0 (via `IAscensionSystem.GetTier`, safe-default, no component created). `TryAscend` never raises tier above `AscensionConstants.MaxTier` (6) nor below 0; a call at max tier is a no-op returning a `NotEligible`/`AtMaxTier` result.
- **Additive baseline fold (INV-24).** For a tracked score, `IStatSystem.Get(entity, score)` includes `AscensionConstants.TierBaselineStep × tier` on top of base + equipment + abilities + progression — pulled fresh from `AscensionEffectContributor.GetModifiers`, with **no** `EffectsComponent` entry and **no** cached field. Tier 0 contributes exactly 0.
- **No reset on ascend.** Ascending mutates only `AscensionComponent.Tier` (and unlock-record state); `ProgressionComponent.Xp`/`Improvements` are untouched. A maxed-Tier-1's effective score after ascending equals its pre-ascend effective score **plus** one baseline step (the progression power is preserved, the baseline is layered).
- **Eligibility seam.** `CanAscend(entity)` returns a structured result (`Eligible` / a typed reason such as `AtMaxTier`). In this slice the admin path bypasses the deferred Objective gate; `CanAscend`'s shape is the seam a future objectives slice fills.
- **Unlock-record seam.** On a successful ascend, `TryAscend` records the new tier's configured unlock ids (`AscensionConstants.UnlocksForTier(tier)`) onto `AscensionComponent.GrantedUnlocks`, **idempotently** (re-recording an already-held id is a no-op). The unlock table is **empty** in prog-2 — no tier configures unlocks yet — so nothing is recorded; what ships is the durable state + `GetGrantedUnlocks` accessor + the `AscendedEvent` a future grant handler consumes. Concrete grants and the grant-execution seam are deferred.
- **Event published once.** Exactly one `AscendedEvent(entityId, newTier, previousTier)` is published per successful ascend by the command (Initiator), never by the system (INV-5/INV-8). A rejected/no-op ascend publishes nothing.
- **Admin boundary save (INV-22).** The `ascend` command calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)` **once** after the mutation, paired with the audit event `PlayerAscendedByAdminEvent` — case (b) admin boundary save; no other save site.
- **Persistence.** `AscensionComponent` is `[Persistent]`; a save→load round-trip preserves `Tier` and granted-unlock-record state. World-content entities (mobs) **never** carry it.
- **Content band tag.** `MobTemplate.TierBand` round-trips through YAML; the live `MobDataComponent` carries the band, sourced from the template at spawn. `MobDataComponent` is `[Persistent]`, **but mob entities are world content (never carry `PersistentEntity`)**, so per the Level-1 gate the band is never snapshotted — its durable form is the YAML template, re-applied each spawn. Inspectable via `setmob band` readback and the Blazor `MobEditor`. (Item bands are deferred to slice 3 with the budget oracle that consumes them.)
- **Functional-validation gate.** With a fixture at Tier 0 facing a Tier-1-banded mob (deadly), an admin `ascend` on the fixture shifts the same mob to medium (the effective-score gap closes by one baseline step); `progress`/`score` reflect the new baseline.

---

## Main flow

1. A privileged session issues `ascend <characterName>` (target defaults to the invoker if omitted). `AscendCommand` resolves the target player entity by connected-session character name (mirrors `SetRespawnCommand`).
2. `AscendCommand` calls `IAscensionSystem.CanAscend(targetEntityId)`. On a non-`Eligible` result (e.g. `AtMaxTier`) it writes the reason and returns — nothing published, nothing saved.
3. On `Eligible`, `AscendCommand` calls `IAscensionSystem.TryAscend(targetEntityId)` → the system creates `AscensionComponent` lazily if absent, increments `Tier` (clamped to `[0, MaxTier]`), records the new tier's configured unlock ids on `AscensionComponent.GrantedUnlocks` (the table is empty in prog-2 → none recorded), and returns an `AscendResult { PreviousTier, NewTier, UnlocksRecorded }`. The system publishes nothing (INV-5).
4. `AscendCommand` calls `IPersistenceSystem.SaveEntityAsync(targetEntityId)` once (INV-22 admin boundary save — the tier and unlock-record state are now durable).
5. `AscendCommand` publishes `AscendedEvent(targetEntityId, NewTier, PreviousTier)` (the milestone fact) and `PlayerAscendedByAdminEvent(invokerEntityId, targetEntityId, NewTier)` (the audit fact), then writes a confirmation line to the invoker.
6. `AscensionNarrationHandler` (priority 80) consumes `AscendedEvent` → writes "You ascend to Tier N." to the ascended player and broadcasts "X ascends to Tier N!" to the room (pure output fan-out).
7. `AdminAuditHandler` (priority 80) consumes `PlayerAscendedByAdminEvent` → one structured audit log line (extends the existing admin-audit fan-in).
8. **Later, on any read:** `IStatSystem.Get(entity, score)` → `IEffectSystem.GetModifiers` sums the DI-collected `IEffectContributor`s, now including `AscensionEffectContributor`, which returns `TierBaselineStep × GetTier(entity)` for the score — fresh, uncached (INV-24). `score`, combat, `progress`, and the banded-mob comparison all reflect the new baseline immediately.

---

## Events fired

| Event | Payload | Published by | Purpose |
|---|---|---|---|
| `AscendedEvent` | `(uint EntityId, int NewTier, int PreviousTier)` | `AscendCommand` (Initiator) | Milestone fact — drives narration, the future unlock-grant handler, future band re-tag/telemetry/achievements. Thin, past-tense. |
| `PlayerAscendedByAdminEvent` | `(uint AdminEntityId, uint TargetEntityId, int NewTier)` | `AscendCommand` (Initiator) | Admin audit fan-in (`AdminAuditHandler`). Mirrors `PlayerRespawnSetByAdminEvent`. |

> No per-read or "tier baseline changed" event — the baseline is compute-on-read (INV-24), and ascend is the single discrete fact worth broadcasting (brief event-granularity call).

---

## Systems / handlers involved

| Piece | Type | New/Reuse | Notes |
|---|---|---|---|
| `IAscensionSystem` / `AscensionSystem` | Domain system | **New** | `GetTier`, `CanAscend`, `TryAscend`, `GetGrantedUnlocks`. Returns result records (`AscendResult`, `AscendEligibility`); publishes nothing (INV-5). Ctor `(EntityService)` — **reads raw `AscensionComponent.Tier` only**, never `IStatSystem`/`IEffectSystem` (avoids the prog-1 DI cycle; the baseline is a pure function of tier). `TryAscend` records `AscensionConstants.UnlocksForTier` (empty table now). |
| `AscensionEffectContributor` | Domain adapter of core `IEffectContributor` port | **New** | Fourth registrant alongside `EquipmentEffectContributor`, `AbilityEffectContributor`, `ProgressionEffectContributor`. `GetModifiers(entityId, score) = TierBaselineStep × GetTier(entityId)` for tracked scores; `GetActive` yields a synthetic `WhileKnown` "ascension.tier" effect for display parity (mirrors `ProgressionEffectContributor`). |
| `IStatSystem` / `EffectSystem` | Core systems | **Reuse** | Unchanged — `EffectSystem.GetModifiers` already DI-collects `IEnumerable<IEffectContributor>`; adding a registrant is zero interface change. |
| `IProgressionSystem` | Domain system | **Reuse (read-only interaction)** | Untouched by ascend — the "no reset" postcondition is that its state is *not* mutated. Both contributors fold independently on read. |
| `AscendCommand` | Initiator (command) | **New** | Admin verb `ascend`; resolves target, calls `CanAscend`/`TryAscend`, boundary-saves, publishes both events. Mirrors `SetRespawnCommand`. |
| `AscensionNarrationHandler` | Handler | **New** | Subscribes `AscendedEvent`, priority 80 (`Notification`); pure output fan-out (INV-8). |
| `AdminAuditHandler` | Handler | **Reuse (extend)** | Add `PlayerAscendedByAdminEvent` to its event list — one more audit row. |
| `IMobBuilderSystem` | Domain system | **Reuse (extend)** | Add `SetMobBand` — dual-write `MobDataComponent` **and** `MobTemplate` (mirrors `SetMobProtection`). |
| `SetMobCommand` | Initiator | **Reuse (extend)** | Add a `band` property branch (mirrors the `protection` branch); range-validates the `int` `0–6`. |
| `MobContentWriter` | Domain system | **Reuse (extend)** | Add `band` to the YAML DTO (omitted when `0`/unbanded). |

---

## Implementation plan — work packages

Three independently-executable packages; the primary agent runs `architecture-reviewer` (code mode) across the combined diff once all land.

### WP-1 — Ascension module: component, system, contributor, constants (the tier spine)

**Scope.** `AscensionComponent` (`[Persistent]`; `int Tier`, `List<string> GrantedUnlocks`). `AscensionConstants` (`MaxTier = 6`, `TierBaselineStep`, the tracked-score set the baseline applies to — a `ProgressionConstants.CombatTracks`-style array, and `UnlocksForTier` — an **empty** per-tier unlock-id table for now; Config Category 3, co-located). `IAscensionSystem`/`AscensionSystem` (`GetTier`, `CanAscend → AscendEligibility`, `TryAscend → AscendResult`, `GetGrantedUnlocks`). `AscensionEffectContributor : IEffectContributor`. `AscensionModule.AddAscensionModule` registering `IAscensionSystem`, the `IEffectContributor`, and (WP-2) the command/handler.
**Files.** `Core/Modules/Ascension/Components/AscensionComponent.cs`, `Core/Modules/Ascension/AscensionConstants.cs`, `Core/Modules/Ascension/Systems/IAscensionSystem.cs` + `AscensionSystem.cs`, `Core/Modules/Ascension/AscensionEffectContributor.cs`, `Core/Modules/Ascension/AscensionModule.cs`; register `AddAscensionModule()` in `Server/CompositionRoot.Register` (NOT `Program.cs` — Blazor `StatSystem` parity, mirrors `ProgressionModule`).
**Out of scope.** Command, handler, band tag, objective gate, concrete unlock content, the grant-execution seam.
**Exit criterion.** Tier 1 tests: baseline fold (`GetModifiers` = step × tier; Tier 0 = 0), `TryAscend` clamp at `MaxTier`, `GetTier` safe default, unlock-record idempotency (empty table → `GetGrantedUnlocks` empty and stable; the record path is idempotent), no-`IStatSystem`-dependency (ctor takes only `EntityService`). Persistence round-trip for `AscensionComponent`.

### WP-2 — Ascend command + narration + audit + events (the tier-up gate)

**Scope.** `AscendedEvent`, `PlayerAscendedByAdminEvent`. `AscendCommand` (admin verb, resolve target, `CanAscend`→`TryAscend`→boundary-save→publish both). `AscensionNarrationHandler` (priority 80). Extend `AdminAuditHandler`'s subscription list. Subscribe `AscendedEvent`/audit handlers in `Server/Program.cs` (mirrors `ExperienceAwardHandler` subscription).
**Files.** `Core/Modules/Ascension/Events/AscendedEvent.cs`, `Core/Modules/Ascension/Events/PlayerAscendedByAdminEvent.cs`, `Core/Modules/Ascension/Commands/AscendCommand.cs`, `Core/Modules/Ascension/Handlers/AscensionNarrationHandler.cs`, `Core/Output/AscensionNarrationMessage.cs` (if a typed message is warranted, else reuse `PlainMessage`); edits to `AdminAuditHandler.cs` and `AscensionModule.cs`/`Program.cs`.
**Dependencies.** WP-1 (`IAscensionSystem`).
**Exit criterion.** Tier 2 tests: `AscendCommand` publishes exactly one `AscendedEvent` + one `PlayerAscendedByAdminEvent` + one `SaveEntityAsync` on success; rejected ascend (at max tier) publishes/saves nothing. Tier 3 flow: admin `ascend` fixture → tier increments → `IStatSystem.Get` reflects the baseline step → `progress`/`score` output changes.

### WP-3 — Mob tier-band tag (authoring + inspection)

**Scope.** A `TierBand` `int` (`0–6`; `0` = unbanded) on `MobTemplate`; carried on the live `MobDataComponent`, sourced from the template at spawn. `SetMobBand` on `IMobBuilderSystem`/`MobBuilderSystem` (dual-write template + component, mirroring `SetMobProtection`). `band` property branch on `SetMobCommand` (range-validates `0–6`). `band` field on `MobContentWriter`'s YAML DTO + the deserializer (omitted when `0`). Blazor `MobEditor` band field. **Mobs only** — item bands are deferred to slice 3 (their consumer, the power-budget oracle, lands there; `ItemDataComponent` is `[Persistent]`, so the item-band persistence shape is a slice-3 design point, not built here).
**Files.** edits to `Core/Modules/Mobs/Templates/MobTemplate.cs`, `Core/ECS/Components/MobDataComponent.cs`, `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` (+ interface), `Core/Modules/Mobs/Commands/SetMobCommand.cs`, `Core/Modules/Mobs/Systems/MobContentWriter.cs` (+ deserializer); Blazor editor field in `Hedron.Web` (`MobEditor`).
**Dependencies.** None on WP-1/WP-2 (parallelizable); the functional-validation gate composes all three.
**Exit criterion.** Tier 4: `MobTemplate.TierBand` YAML round-trip; a **mob fixture carries no `PersistentEntity`** (world content) so the band never reaches a persistence snapshot (INV-23). Tier 2: `setmob band` dual-write assertion (component + template both updated). The band value is authored, inspectable, and drives the functional-validation fixture's deadly→medium demonstration.

---

## Content tooling impact

Required (INV-18) — this slice adds gameplay state, so it ships authoring + inspection in the same PR:

- **Admin command — `ascend <characterName>`** (new): the tier-up trigger for this slice. Admin-gated (`AdminRequirement`). Also the functional-validation hook.
- **Admin command — `setmob band <blueprintId> <tier>`** (new property branch): authors the mob tier-band tag, dual-writing the live `MobDataComponent` and the `MobTemplate` (survives `reload`). Range-validates `0–6`.
- **YAML shape — `band:`** on mob templates (a single int field; omitted when `0`/unbanded). Symmetric read (deserializer) + write (`MobContentWriter`).
- **Blazor editor — band field** on `MobEditor` (headless authoring parity).
- **Inspection.** Tier is inspectable in-game via `score`/`progress` (baseline reflected in effective scores) and, if a typed line is added, an explicit "Tier N" row. The mob band is inspectable via `setmob` readback, the editor, and the functional-validation fixture.
- **`TemplateRegistry` entries.** No new blueprint kind — the band rides the existing mob templates.

> **Deferred (noted, not built):** no `setascension`/hand-set-tier admin command beyond `ascend` (tracked as a follow-up like prog-1's `setprogress`); the player-facing Ascension-Objective gate; concrete tier unlocks + the grant-execution seam + specialization-on-ascend *selection* UX; **item** tier-bands (slice 3, with the power-budget oracle that consumes them). The band is a *tag*, not a power-budget oracle — the banding math + `IPowerBudgetSystem` is slice 3.

---

## Cross-cutting surfaces stressed

Required (INV-19). Ground-rule-9 audit; the brief's Family-disposition rows map here (`Build now` → *Gap exposed*/handled-in-slice; `Defer` → *Acknowledged debt*).

| Surface | Classification | Rationale |
|---|---|---|
| **ECS queries** | Adequate | `HasComponent<AscensionComponent>`/`TryGet` — standard component access (INV-4). |
| **Stat-read contributor port (`IEffectContributor`)** | **Adequate** | The whole tier-baseline seam reuses the existing port (brief open-Q#5 resolved: ride the progression contributor's proven pattern, *not* a new `IScalingSystem`). Fourth registrant, zero interface change — the port already generalized to N contributors in slice 11-a. **No `IScalingSystem` / Spine-D seam is stood up here** (deferred; it can subsume this later without changing callers). |
| **Commands** | Adequate | `AscendCommand` follows the established `ICommand` + `AdminRequirement` + `CommandArgumentSchema` shape (mirrors `SetRespawnCommand`); the `band` branch reuses the `setmob` property-branch idiom (mirrors `protection`). No new command framework. |
| **Events / event bus** | Adequate | Two thin past-tense events; publish is the Initiator's (INV-5/INV-8). `AdminAuditHandler` extension is one row on an existing fan-in. |
| **Persistence** | Adequate | `AscensionComponent` `[Persistent]` on a player entity already carrying `PersistentEntity` (no new opt-in machinery). The mob band on `MobDataComponent` (`[Persistent]`) never snapshots because mob entities are world content (Level-1 not opted in) — durable form is the YAML template. Admin boundary save is case (b). See the persistence opt-in audit below. |
| **Output / broadcast** | Adequate | `AscensionNarrationHandler` reuses `IBroadcastSystem` room fan-out (mirrors existing notification handlers). |
| **Content templates** | Adequate | Band tag rides `MobTemplate` + its writer/deserializer/editor — the established authored-world-content pattern (mirrors `protection`). No new template kind. (Item band deferred to slice 3.) |
| **Configuration** | Adequate | `AscensionConstants` is Category-3 System Math/Balance, co-located, mirroring `ProgressionConstants`; promotion to YAML deferred per OD-2 (slice-4 sim is the likely trigger). |
| **Unlock-grant path (effect-grant seam)** | **Acknowledged debt** | prog-2 ships **only** the unlock-*record* seam: `AscensionComponent.GrantedUnlocks` + `GetGrantedUnlocks` + the `AscendedEvent` a future grant handler consumes, with an **empty** unlock table. The grant-*execution* seam (`GrantFlag`/`GrantAbility` are unimplemented `EffectKind` values) and concrete unlock content are **deferred** (brief: *Defer — grant seam now, selection UX later*). No hand-rolled framework lands now (empty table + one accessor — restraint), so this is not *Gap exposed*. When unlock content + the execution seam are designed, they wire into `TryAscend`/the event without changing this slice's shape. Tracked in `backlog.md`. |
| **Objective gate (`IObjectiveSystem`)** | **Acknowledged debt** | The real player-facing ascend trigger is an Ascension Objective; `IObjectiveSystem` is unbuilt. `CanAscend` is designed as the seam that gate will call; the admin `ascend` command is the interim trigger. Owner-resolved: deferred to a future objectives slice. Tracked in `backlog.md`. |
| **Scaling seam (`IScalingSystem`, Spine D)** | **Acknowledged debt** | The baseline rides the effect-contributor port instead of a dedicated scaling system (deferred, brief Family-disposition). Slice 3's `IPowerBudgetSystem` and a later Spine-D `IScalingSystem` can subsume the baseline computation without changing callers. Tracked in `backlog.md`. |

**No `Gap exposed` findings.** Every surface this slice touches has an established shape (the contributor port, the command idiom, the admin-boundary-save, the world-content template tag). The deferrals above are *acknowledged debt with owner rationale + backlog entries*, not silently-absorbed gaps.

### Persistence opt-in audit (mandatory)

**Level 1 — entity domain classification.**
- `AscensionComponent` is attached only to **persistent player entities** (already carry `PersistentEntity`) — created lazily by `TryAscend`, exactly like `ProgressionComponent`'s first-award attach. No new `PersistentEntity` add is introduced by ascend (the player already has it); the boundary save persists the mutation.
- **Mobs are world content** — they do **not** carry `PersistentEntity`. The tier-**band** tag rides `MobTemplate` (YAML durable) and the live `MobDataComponent`. `MobDataComponent` **is `[Persistent]`**, but because a mob entity is never opted in (no `PersistentEntity`), the Level-1 gate means it is **never snapshotted** — no band data reaches SQLite; the band is re-applied from the template on each spawn. No entity transitions domains in this slice.

**Level 2 — component inclusion.**
- `AscensionComponent` → **`[Persistent]`**: holds player tier + granted-unlock-record state that must survive restart. Correct.
- `MobDataComponent` (the band's live carrier) → **already `[Persistent]`**, unchanged by this slice. Adding a `TierBand` field does not change its persistence behavior: its carrier (a mob) is world content and never opted in, so the field never persists in practice; the durable form is the YAML template. **No item-band field is added** — item bands (and their `[Persistent]` `ItemDataComponent` shape) are deferred to slice 3.

**Level 3 — save-on-change scope.**
- The single `SaveEntityAsync` in `AscendCommand` is an **admin boundary save (case b)**: an admin-gated command mutating an already-persistent player through a domain system, saving once after the mutation, paired with the `PlayerAscendedByAdminEvent` audit event. Confirmed compliant with INV-22.
- No handler calls `SaveEntityAsync` for a runtime state change. `AscensionNarrationHandler` is pure output. `AscensionEffectContributor` never persists (compute-on-read).

---

## Flows introduced or modified

Required (INV-17).

- **New — `flow-32 — Ascension journey (tier-up · unlock-record · baseline fold)`.** Trigger: privileged session issues `ascend`. Traces `AscendCommand` → `IAscensionSystem.CanAscend`/`TryAscend` → boundary save → `AscendedEvent`/`PlayerAscendedByAdminEvent` publish → `AscensionNarrationHandler`/`AdminAuditHandler` → the later `IStatSystem.Get` contributor fold. Add the file and an index row to `flows/README.md`. Mirrors the structure of [flow-31](../architecture/flows/flow-31-progression-award.md).
- **Modified — [flow-31 — Progression journey](../architecture/flows/flow-31-progression-award.md)** (the contribute-on-read leg): the "later `IStatSystem.Get`" fold now sums a **second** contributor (`AscensionEffectContributor`) alongside `ProgressionEffectContributor`. Update flow-31's closing note (or cross-link from flow-32) so the stat-read fold is not described as progression-only. The slice PR must reconcile this per the flows update rule.
- **Touch — [flow-08 — Admin authoring journey](../architecture/flows/flow-08-admin-room-creation.md)**: `setmob band` is a new property branch on the existing builder-verb flow — a one-line mention that `band` joins the authored mob-property set (no structural change to the flow).

---

## Reference catalogs & agent tooling updated on ship

Required (INV-16 / INV-29 / INV-20). Established precedent — the parallel prog-1 pieces are already catalogued (`reference/systems.md` `ProgressionSystem`/`ProgressionEffectContributor`, `reference/components.md` `ProgressionComponent`, `reference/handlers.md` progression handler), so the equivalent Ascension rows are an obligation, not a new pattern.

**Reference catalogs (INV-16 / INV-29):**
- `docs/reference/systems.md` — add `IAscensionSystem`/`AscensionSystem` and `AscensionEffectContributor` (alongside the existing progression rows).
- `docs/reference/components.md` — add `AscensionComponent`; note the `MobDataComponent.TierBand` field extension.
- `docs/reference/handlers.md` — add `AscensionNarrationHandler`; note `AdminAuditHandler`'s new `PlayerAscendedByAdminEvent` subscription.
- `docs/reference/commands.md` — add `ascend`; note the `setmob band` property branch.

**Agent tooling (INV-20):**
- `.claude/skills/edit-progression-system/SKILL.md` — turn the forward-looking "(Ascension, slice 2)" reference into a shipped-symbol reference (`IAscensionSystem`, `AscensionEffectContributor`, `AscensionConstants.TierBaselineStep`), and note the DI-cycle guardrail now has a **second** confirming precedent (Ascension alongside Progression: a contributor's backing system reads raw component data, never a computed value).
- **No change needed** to `add-command`, `add-domain-system`, or `add-component` — the `ascend`/`band`/component shapes follow their existing guidance verbatim (verified at the spec gate).

---

## Test plan / Verification

Required (INV-25). Derived from the Postconditions and Main flow, per the rubric in [`../architecture/07-testing.md`](../architecture/07-testing.md).

**Tier 1 — system unit (`Hedron.Tests/Ascension/`).**
- `AscensionSystemTests`:
  - `GetTier` returns 0 for an entity with no component; creates nothing.
  - `TryAscend` from 0 → 1 sets `Tier`, returns `AscendResult { PreviousTier=0, NewTier=1 }`.
  - `TryAscend` at `MaxTier` (6) is a no-op returning the at-max result; `Tier` unchanged.
  - Unlock-record idempotency: with the (currently empty) `UnlocksForTier` table, `GetGrantedUnlocks` returns empty and stable across ascends; the record path does not duplicate (exercised via a test-seeded id where the mechanism allows, else asserts empty-stable — the substantive assertion lands once unlocks are wired).
  - `CanAscend` returns the typed reason (`AtMaxTier`) at max; `Eligible` otherwise (admin path).
  - **No-reset assertion:** `TryAscend` does not touch `ProgressionComponent` (seed both components; assert progression Xp/Improvements unchanged post-ascend).
- `AscensionEffectContributorTests`:
  - `GetModifiers(entity, trackedScore) == TierBaselineStep × tier`; Tier 0 → 0.
  - Never-materialized: after a `Get` read, the entity has no `EffectsComponent` entry for the baseline (mirrors `ProgressionEffectContributorTests`).
  - Baseline is additive on top of a progression improvement (seed both; assert `Get` = base + progression step + tier baseline).

**Tier 2 — handler / orchestration.**
- `AscendCommandTests`: on success, `RecordingEventBus` captured exactly one `AscendedEvent(newTier, previousTier)` **and** one `PlayerAscendedByAdminEvent`, and `IPersistenceSystem.SaveEntityAsync` was called once (fake persistence). On a max-tier target, no event and no save. Non-privileged invoker is rejected by the auth gate (schema-level; covered by the command-auth guard, not re-tested here).
- `SetMobCommand` `band` branch: dual-write assertion — after `setmob band <id> 2`, both the live `MobDataComponent` and `MobTemplate.TierBand` read 2. Out-of-range (`> 6`) is rejected.

**Tier 3 — flow (`AscensionFlowTests`).**
- The Main-Flow executable: admin `ascend` fixture → `AscensionComponent.Tier` increments → `IStatSystem.Get(fixture, trackedScore)` increases by exactly one `TierBaselineStep` → `AscendedEvent` published → `progress`/`score` typed output reflects the baseline. Real `IAscensionSystem` + `AscendCommand` + dispatching bus + fake persistence.
- **Functional-validation gate embedded here:** seed a Tier-1-banded mob and a Tier-0 fixture; assert the effective-score gap (fixture vs. mob) is "deadly" pre-ascend and "medium" post-ascend by comparing `IStatSystem.Get` values before/after (the deadly→medium demonstration in test form — no banding oracle needed, just the baseline delta).

**Tier 4 — persistence round-trip (add to `Hedron.Tests/Persistence/RoundTripTests.cs`).**
- `AscensionComponent` save→load into a fresh world preserves `Tier` and granted-unlock-record state.
- A mob fixture carries **no `PersistentEntity`** (world content) and never carries `AscensionComponent`; its band lives only on the template and is **absent from any snapshot** (validates the Level-1 world-content gate).

**Tier 5 — architecture-guard.** Covered by the existing reflection suite (no new guards): `AscensionSystem` has no `IEventBus` field (no-bus-in-systems); `AscensionComponent` is data-only; DI-smoke resolves the new registrations. No `Random.Shared`/`DateTime.Now` — the tier baseline and ascend logic are pure state functions (no `IRandom` seam needed this slice; **no testability gap** — everything is deterministic from component state).

**Skipped (with reason).**
- Exact narration prose (`AscensionNarrationHandler`) — assert message *type/audience* only (Tier 2 posture), never wording.
- The thin `ascend` parse→system→publish plumbing beyond the Tier-2 event/save assertions — covered by the flow test.
- Pure-data `AscensionComponent` getters/setters and per-module DI registration — covered by INV-3 guard + DI-smoke.
- The Blazor `MobEditor` band field UI — presentation; the dual-write correctness is the tested part.

> **Testability note (INV-26).** This slice introduces **no** chance/time-dependent logic — the tier baseline is `step × tier` and ascend is a clamped increment, both pure functions of component state. No injected seam is needed; there is no testability gap to surface.

---

## Design notes

> Durable seam rationale (folded from the program brief's `## Architecture brief` + this slice's owner-resolved decisions). Survives disintegration into `docs/features/progression/` on ship.

### Additive baseline, no reset — the overlap semantics fall out (brief open-Q#1 → RESOLVED)

Tier confers a **flat additive power baseline** across scores; a maxed lower tier keeps all its per-track progression power and ascending *layers the baseline on top*. The XP-reset/rescale-on-ascend mechanic is **dropped** (the brief's "Defer — probably unnecessary" is now a firm no). This is why the deadly→medium overlap works with no extra machinery: a Tier-1 character lacks the Tier-2 baseline step, so a Tier-2-tuned mob out-scales them (deadly); ascending grants the step and the same mob normalizes to medium (the "fresh climb" feel). A Tier-2 character in Tier-1 content keeps the step (comfortably over-scaled). Bands **overlap** — a maxed lower tier can reach into the next band before formally ascending (the pinnacle-activity → higher-difficulty hook).

### The tier baseline rides the existing `IEffectContributor` port — not a new scaling system (brief open-Q#5 → RESOLVED)

`AscensionEffectContributor` is a **fourth registrant** on the core-owned `IEffectContributor` port (equipment, abilities, progression, ascension), folded by `IStatSystem.Get` on read — never stored (INV-24). This reuses the exact pattern slice 1 proved. A dedicated Spine-D `IScalingSystem` is **deferred**; riding the contributor first is the cheaper start and a later scaling system (or slice-3's `IPowerBudgetSystem`) can subsume the baseline computation without changing any caller. **DI-cycle guard (prog-1 as-built rule):** `AscensionSystem`'s backing input is **raw `AscensionComponent.Tier`** read via `EntityService` — never a computed `IStatSystem`/`IEffectSystem` value. This is trivially satisfied (the baseline is a pure function of tier) but is stated so nobody wires `IAscensionSystem` → `IStatSystem` and re-creates the `IStatSystem` → `IEffectSystem` → contributor → backing-system → `IStatSystem` cycle.

### Ascension is a new module, a scalar + unlock flags (R1)

`Core/Modules/Ascension/` is its own module (not folded into Progression), matching gameplay-model R1 ("`AscensionComponent` stays a scalar + unlock flags"). `AscensionComponent` is the tier scalar `int 0–6` plus granted-unlock-record state. Horizontal theming (per-area aspect attunement) is separate and deferred (R1).

### Tier-up gate: admin now, Objective later — `CanAscend` is the seam

Eligibility, `AscendedEvent`, and the unlock-record seam all land and are testable in this slice. The **only trigger now is an admin `ascend` command** (plain verb, structural privilege gate — no sigil, per owner + memory). The real player-facing gate is an **Ascension Objective**; `IObjectiveSystem` is unbuilt, so `CanAscend` is deliberately shaped as the seam a future objectives slice fills (it will call `CanAscend`, which will consult the objective log). Do not build objective logic here.

### Content band tagging is lightweight, mobs-only — threat is emergent from the baseline

A tier-band tag on **mobs** (`MobTemplate` + live `MobDataComponent`) is **authored + inspectable** (INV-18 tooling in this PR). Mechanical threat is **emergent from the additive baseline** — a Tier-N mob is simply *tuned to Tier-N baseline stats*; there is **no separate threat multiplier**. Bands **overlap**. `TierBand` is a plain `int 0–6` (tier is a numeric scalar; `setmob band` range-validates), not an enum — an enum adds ceremony without value for a numeric band. **Item bands are deferred to slice 3**: nothing in prog-2 reads a band at runtime (the power-budget oracle that consumes bands is slice 3), and `ItemDataComponent` is `[Persistent]` (player-owned items), so an item-band field is a persistence-shape decision best made alongside its consumer — adding item-band authoring now with no reader is over-build (INV-19 restraint). The full power-**budget** oracle + banding math (`IPowerBudgetSystem`) is **slice 3 (prog-3)** — this slice ships only the mob tag + authoring + the deadly→medium functional demonstration.

### Doc fix (INV-15) — `IdentityComponent.Tier` does not exist

The gameplay-model's note that "`IdentityComponent.Tier` already exists as the seed" (`docs/design/gameplay-model.md` ~lines 443, 524) is an **INV-15 doc error** — there is no `IdentityComponent` in code (verified). Tier lives on the **new `AscensionComponent`**. This slice's PR **corrects that note** in the gameplay-model (~lines 443 & 524) **and** `docs/reference/components-planned.md:13` (which carries the same `IdentityComponent.Tier` seed row — verified at the spec gate) to point at `AscensionComponent`, in the slice that lands it. `AttributesComponent.Level` (the components-reference calls it "vestigial, superseded by Ascension tier") is not repurposed — tier is a distinct scalar; the Level-vestigial disposition is a separate cleanup, not in scope here.

### Unlocks: the record seam ships; grant-execution + content are deferred

Ascending is designed to grant tier unlocks (aspects/abilities/flags), but the grant-*execution* seam **does not exist** — `GrantFlag`/`GrantAbility` are unimplemented `EffectKind` enum values (`Core/Modules/Effects/Effect.cs`, verified), not a callable path. prog-2 therefore ships only the unlock-*record* seam: `AscensionComponent.GrantedUnlocks` + `GetGrantedUnlocks` + the `AscendedEvent` a future grant handler will consume. The `AscensionConstants.UnlocksForTier` table is **empty** (no tier configures unlocks yet). When unlock content **and** the grant-execution seam are designed, they wire into `TryAscend`/the event without changing this slice's shape (the `XpSource`-style additive-promotion pattern prog-1 established). The player **selection UX** is deferred beyond the grant seam (brief Family-disposition). If per-tier grant logic later repeats ≥3× or needs data-authoring, promote to an unlock registry (backlog).

---

## Related

- [`progression-and-balance.md`](progression-and-balance.md) — the program brief this slice extends (`## Architecture brief`, `Resolved decisions`, `Slice 2` row). **Authoritative seed.**
- [`../roadmap/completed/progression-substrate.md`](../roadmap/completed/progression-substrate.md) — slice 1 as-built; the shapes this slice mirrors (contributor registration, DI-cycle rule, `[Persistent]` player-only component, admin-boundary-save deferral).
- [`../features/progression/progression.md`](../features/progression/progression.md) · [`progression-system.md`](../features/progression/progression-system.md) — the feature this slice extends; the contributor fold and DI-cycle rationale.
- [`../features/character-stats/stat-system.md`](../features/character-stats/stat-system.md) · [`../features/effects/effect-system.md`](../features/effects/effect-system.md) — the `IStatSystem.Get`/`IEffectSystem.GetModifiers` read seam and the `IEffectContributor` port precedent (equipment, abilities, progression).
- [`../design/gameplay-model.md`](../design/gameplay-model.md) — §6 R1 (Ascension: vertical scalar 0–6, `AscensionComponent` = scalar + unlock flags), the overlap map row (Ascension tier-up = E+D+A), and the INV-15 doc note this slice corrects.
- [`../features/mobs/mob-system.md`](../features/mobs/mob-system.md) — the builder/template/writer authoring pattern the band tag mirrors (`SetMobProtection`).
- [flow-31 — Progression journey](../architecture/flows/flow-31-progression-award.md) — the contribute-on-read leg this slice extends with a second contributor; [flow-08](../architecture/flows/flow-08-admin-room-creation.md) — the admin-authoring flow the `band` branch touches.
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-24 (contribute-on-read), INV-5/INV-8 (systems return, initiators publish), INV-22/23 (persistence two-level opt-in; admin boundary save), INV-15 (fix the doc first), INV-16/INV-29 (reference catalogs), INV-18 (content tooling), INV-19 (infrastructure parity), INV-17 (flows), INV-20 (agent/skill currency), INV-25/26 (verification + determinism).
