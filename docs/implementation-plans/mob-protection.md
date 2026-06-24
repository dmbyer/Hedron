# Mob Protection

> **Status:** `planned` — architecture-tier seed from the `architecture-advisor` intake (the shopping feature, split into slices 12a/12b/12c). This is **12b — a cross-cutting mob-configuration slice**, independent of trade and parallelizable with 12a (Item value). 12c (Shopping) consumes it to protect safe-area shopkeepers. The `implementation-planner` extends this into the full template ([`README.md`](README.md)).

**Actors:** Administrator (configures a mob's protection), Player (cannot attack or status-affect a protected mob), System (combat and effect resolution honor the flags).

**Module:** cross-cutting. The `ProtectionComponent` lives under `Core/ECS/Components/` (queryable without a domain dependency, like `MobDataComponent`); the **combat gate** lands in `Core/Modules/Combat/`; the **effect gate** in `Core/Modules/Effects/` (`EffectSystem.Apply`); authoring rides the existing **Mob** tooling. Feature home on ship: a new section in [`../features/mobs/`](../features/mobs/) (the mob-configuration owner), cross-linked from combat + effects. Because the slice changes the `IEffectSystem.Apply` return contract, [`../features/effects/effect-system.md`](../features/effects/effect-system.md) (which documents that contract) is updated in the same PR (INV-16/INV-28).

## Description

A mob (or any entity) may be configured with **protection flags** along two independent axes: `Untargetable` (cannot be the target of an attack) and `EffectImmune` (rejects **every** effect, beneficial or harmful). A shopkeeper standing in a safe primary area sets both, so it cannot be killed or status-affected; a wandering shopkeeper or an ordinary creature sets neither and follows normal rules. The flags are authored on the mob template; combat target resolution and `IEffectSystem.Apply` each read them independently and refuse the action with a clear message rather than letting it land as a no-op.

## Design notes

- **Two independent axes, not one bundled bool.** `Untargetable` (no attack) and `EffectImmune` (no effects) are separate flags in a `[Flags]` enum so a future entity can have one without the other (a boss immune to crowd-control but killable; a passive NPC affectable but un-attackable). A protected shopkeeper simply sets both.
- **The flag is data; the refusal lives with each rule's domain (mechanism vs. consequence).** `ProtectionComponent` carries no logic. The *attack refusal* is a combat-domain decision (a `CanBeAttacked`-style check the combat system owns, surfaced by `KillCommand` and the ability-targeting pipeline); the *effect refusal* is `EffectSystem`'s (`Apply` returns an "immune" result). Putting the gate in each owning domain — not in a shopping or mob system — is what keeps invulnerability a general entity property rather than a trade concern.
- **Cross-cutting, not shopping-owned.** Invulnerability/immunity is the general primitive behind safe-zone NPCs, future PvP-safe players, room "safe" flags, and sanctuary effects. It ships in its own slice precisely so Shopping (12c) *consumes* it rather than *owning* it.
- **Effect immunity blocks beneficial effects too.** Per explicit design intent, an `EffectImmune` entity rejects positive effects (heals, buffs) as well as negative ones — protection means "nothing lands," not "nothing harmful lands."
- **Refuse at target/initiation, don't absorb.** An attack on an `Untargetable` mob is refused at resolution with a message ("X is protected and cannot be attacked"), not resolved as a zero-damage hit — clearer to the player and cheaper than running a no-op round.

## Preconditions

- An administrator can author mobs via `setmob` and the Blazor `MobEditor` (slices 6, 8, content-tooling platform shipped).
- Combat initiation flows through `KillCommand` → `ICombatSystem` and the ability-targeting path through `AbilityInvocationPipeline` → `ICombatSystem.ResolveAbilityStrike` (slices 9, 11-b).
- Effect application flows through `IEffectSystem.Apply`, called by `AffectCommand` and `AbilitySystem.Activate` (effects feature, slice 11-b).
- `MobTemplate` is the durable spawn definition; `MobContentWriter`/`MobTemplateDeserializer` round-trip its YAML; `MobTemplate.Apply` seeds the live entity (currency-foundation precedent).
- A target mob exists in the player's room and is resolvable by `ICombatSystem.TryFindTargetInRoom`.

## Postconditions

- A `ProtectionComponent` (`[Flags]` enum `ProtectionFlags { None, Untargetable, EffectImmune }`) exists under `Core/ECS/Components/`, **not** `[Persistent]`.
- `ICombatSystem.CanBeAttacked(uint targetEntityId)` returns `false` iff the target carries `ProtectionComponent` with the `Untargetable` flag set; `true` otherwise (including no component).
- `KillCommand` and `AbilityInvocationPipeline`'s offensive-target path both consult `CanBeAttacked` before entering combat / resolving a strike; on refusal neither transitions state, attaches `CombatStateComponent`, deducts HP, nor publishes `CombatStartedEvent`/`AbilityStrikeResolvedEvent`.
- `IEffectSystem.Apply` returns a structured immune result (no `Effect` mutation, no `EffectsComponent` change) when the target carries `ProtectionComponent` with `EffectImmune` set — for **both** beneficial and harmful definitions.
- `AffectCommand` and `AbilitySystem.Activate` surface the immune result (no `EffectAppliedEvent` published for an immune target).
- The actor receives a clear refusal message in each gate; an unprotected mob behaves exactly as before (no regression).
- `IMobBuilderSystem.SetMobProtection` dual-writes the live `ProtectionComponent` and `MobTemplate.Protection`; `setmob protection <flags>` and the `MobEditor` author it; `MobTemplate.Protection` survives the YAML write→read round-trip; a re-spawned mob carries no `PersistentEntity` and no SQLite row for the component.

## Main flow

### Gate A — attack on an `Untargetable` mob

1. Player sends `kill shopkeeper` (or an offensive `cast`/skill targeting the shopkeeper).
2. The initiator resolves the target via `ICombatSystem.TryFindTargetInRoom` (existing).
3. The initiator calls `ICombatSystem.CanBeAttacked(targetEntityId)` (new shared query).
4. `CanBeAttacked` reads `ProtectionComponent` via `HasComponent`/`TryGet` and returns `false` because `Untargetable` is set.
5. The initiator writes a refusal message ("The shopkeeper is protected and cannot be attacked.") and returns — **before** any `TryEnterState`, `StartCombat`, HP change, or `CombatStartedEvent`/strike.
6. No combat begins; world state is unchanged.

### Gate B — effect `Apply` on an `EffectImmune` mob

1. An admin runs `affect shopkeeper empower` (beneficial) or an offensive ability routes a debuff through `AbilitySystem.Activate` → `IEffectSystem.Apply`.
2. `IEffectSystem.Apply` reads `ProtectionComponent` on the target via `TryGet` at the top of the method.
3. `EffectImmune` is set → `Apply` returns `EffectApplyResult.Immune` (new structured result) without constructing/storing an `Effect` or touching `EffectsComponent` — regardless of effect `Category` (beneficial or harmful).
4. `AffectCommand` surfaces "The shopkeeper is immune and the effect did not take hold."; the ability pipeline treats the immune result like a non-application (no `EffectAppliedEvent`).
5. No effect is stored; no modifiers change; world state is unchanged.

## Events fired

- **None new.** Both gates are command/apply-time refusals surfaced to the actor as messages (Architecture brief: no observer/contributor port).
- **Suppressed (not new):** on refusal, the existing `CombatStartedEvent` / `AbilityStrikeResolvedEvent` (Gate A) and `EffectAppliedEvent` (Gate B) are **not** published. The authoring command reuses the existing `MobPropertySetByAdminEvent` (audit) on a successful `setmob protection`.

## Systems / handlers involved

| Piece | Role | New / reused |
|---|---|---|
| `ProtectionComponent` | flag carrier, `Core/ECS/Components/` | **new** |
| `ICombatSystem.CanBeAttacked` / `CombatSystem` | attack gate (≥2-consumer shared query, INV-19) | **new method on reused system** |
| `KillCommand` | initiator A, calls `CanBeAttacked` | reused (edit) |
| `AbilityInvocationPipeline` | initiator A (offensive path), calls `CanBeAttacked` | reused (edit) |
| `IEffectSystem.Apply` / `EffectSystem` | effect gate, returns immune result | reused (signature/return change) |
| `EffectApplyResult` (or nullable + reason) | structured immune result | **new type** |
| `AffectCommand` | initiator B, surfaces immune result | reused (edit) |
| `AbilitySystem.Activate` | initiator B (offensive effect path), surfaces immune result | reused (edit) |
| `IMobBuilderSystem.SetMobProtection` / `MobBuilderSystem` | dual-write live + template (mirrors `SetMobType`) | **new method on reused system** |
| `SetMobCommand` | `protection` property branch | reused (edit) |
| `MobTemplate` / `MobTemplateDeserializer` / `MobContentWriter` | `Protection` field + YAML round-trip | reused (edit) |
| `MobTemplate.Apply` | seeds `ProtectionComponent` when flags non-`None` (mirrors `CurrencyLoot` opt-in) | reused (edit) |
| `MobEditor.razor` | protection checkboxes/select | reused (edit) |

## Implementation plan — work packages

### WP-1 — Protection component + dual gate (combat + effects)

- **Scope:** add `ProtectionComponent` and `ProtectionFlags` (`Core/ECS/Components/`, no `[Persistent]`). Add `ICombatSystem.CanBeAttacked(uint)` + `CombatSystem` impl (flag read). Wire the refusal at both initiator sites: `KillCommand` and `AbilityInvocationPipeline` (offensive-target branch). Change `IEffectSystem.Apply` to return a structured immune result (new `EffectApplyResult` enum/record, or a documented `(Effect?, ImmuneReason)` shape) and read `ProtectionComponent.EffectImmune` at method entry, returning immune for both beneficial and harmful definitions. Update `AffectCommand` to surface the immune outcome. `AbilitySystem.Activate` (a domain system that does **not** publish — INV-5) simply **excludes the immune effect from its returned `AppliedEffects`**, so `AbilityInvocationPipeline` (the initiator that publishes) emits no `EffectAppliedEvent` for it; no bus interaction moves into the system.
- **Files:** `Core/ECS/Components/ProtectionComponent.cs` (new); `Core/Modules/Combat/Systems/ICombatSystem.cs`, `CombatSystem.cs`; `Core/Modules/Combat/Commands/KillCommand.cs`; `Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs`; `Core/Modules/Effects/Systems/IEffectSystem.cs`, `EffectSystem.cs`; `Core/Modules/Effects/Commands/AffectCommand.cs`; `Core/Modules/Abilities/Systems/AbilitySystem.cs`.
- **Dependencies:** none (foundation package).
- **Out of scope:** authoring tooling (WP-2); mob aggro skip (future).
- **Exit criterion:** unit tests prove `CanBeAttacked` returns `false` for `Untargetable`; `Apply` returns immune (no `Effect`, no `EffectsComponent` change) for `EffectImmune` on both a beneficial and a harmful definition; an unprotected target is unaffected.

### WP-2 — Authoring: builder system, command, template round-trip, editor

- **Scope:** add `IMobBuilderSystem.SetMobProtection(uint, ProtectionFlags)` dual-writing live `ProtectionComponent` + `MobTemplate.Protection` (mirror `SetMobType`). Add `MobTemplate.Protection` field; seed `ProtectionComponent` in `MobTemplate.Apply` only when flags ≠ `None` (mirror `CurrencyLoot` opt-in). Add YAML round-trip in `MobContentWriter`/`MobTemplateDeserializer` (store as flag-name string list or CSV; parse case-insensitively, log+skip unknowns — `CurrencyLoot` precedent). Add the `protection` property branch to `SetMobCommand` (parse flag tokens; reuse `MobPropertySetByAdminEvent` + `_contentWriter.WriteAsync`). Add a protection control row to `MobEditor.razor`.
- **Files:** `Core/Modules/Mobs/Systems/IMobBuilderSystem.cs`, `MobBuilderSystem.cs`; `Core/Modules/Mobs/Templates/MobTemplate.cs`; `Core/Modules/Mobs/MobTemplateDeserializer.cs`; `Core/Modules/Mobs/Systems/MobContentWriter.cs`; `Core/Modules/Mobs/Commands/SetMobCommand.cs`; `Hedron.Web/Components/Pages/MobEditor.razor`.
- **Dependencies:** WP-1 (`ProtectionComponent`, `ProtectionFlags`).
- **Out of scope:** player-facing authoring (PvP-safe — deferred per Open questions).
- **Exit criterion:** `SetMobProtection` dual-writes; `setmob <id> protection untargetable,effectimmune` updates both; YAML round-trip test passes; `MobEditor` renders + saves the flags.

> Primary agent runs `architecture-reviewer` (code mode) across the combined WP-1+WP-2 diff once both land.

## Content tooling impact

- **Admin command:** `setmob <blueprintId> protection <flags>` — new property branch (`<flags>` = comma/space-separated `untargetable`, `effectimmune`, or `none`). Reuses `setmob`'s dual-write (live entity + template), `_contentWriter.WriteAsync`, and `MobPropertySetByAdminEvent` audit. Help text in `SetMobCommand.LongDescription` extended.
- **YAML shape:** `MobTemplate.Protection` serialized under a `protection:` key (flag-name list), absent/`None` ⇒ no component (opt-in default, mirrors `currencyLoot`). Unknown flag names logged-and-skipped on deserialize.
- **Blazor editor:** a "Protection" fieldset in `MobEditor.razor` (two checkboxes: Untargetable, Effect-immune) binding `_template.Protection`, saved through the existing `IContentDefinitionCatalog.SaveAsync` path. No new catalog plumbing.
- **Inspection:** authored value visible via the editor and the mob YAML; INV-18 satisfied in-slice.

## Cross-cutting surfaces stressed

- **ECS queries — Adequate.** Both gates read via `HasComponent`/`TryGet<ProtectionComponent>` (INV-4). No new query infrastructure.
- **Commands — Adequate.** `setmob protection` is a new branch on an existing admin command using the established argument schema, dual-write, and audit-event pattern; no new framework.
- **Output — Adequate.** Refusal/immune messages use existing `PlainMessage`/`OutputSeverity`/`OutputCategory`.
- **Content templates — Adequate.** `MobTemplate` + writer/deserializer + `Apply` follow the `CurrencyLoot` opt-in precedent exactly; no new tooling primitive.
- **Persistence — Adequate (opt-in audit below).** No new persistent shape; the gate reads are transient.
- **Cross-system shared query — Gap closed in-slice (INV-19).** `CanBeAttacked` has ≥2 consumers (`KillCommand`, `AbilityInvocationPipeline`); it lands as **one** method on `ICombatSystem`, not two inline flag reads. This is the slice-2-style miss the audit exists to catch; it is resolved by the shared-query placement, not absorbed.
- **Effect-system result contract — Gap exposed → closed in-slice.** `IEffectSystem.Apply` currently returns `Effect?` where `null` already means "not applied (HighestWins lost)". An `EffectImmune` refusal needs a **distinguishable** reason so callers can phrase "immune" vs. "out-stacked". Disposition: introduce a small structured result (`EffectApplyResult { Applied(Effect) | NotApplied(reason) }` or an `enum ImmuneReason` companion) **in WP-1** rather than overloading `null`. Surfaced here, not silently absorbed; framework lands with the slice.

### Persistence opt-in audit (INV-22/INV-23)

- **Level 1 — entity domain:** the only entity construction path touched is `MobTemplate.Apply` (world content). Mobs are fresh-spawned from YAML on startup and never carry `PersistentEntity`. No new persistent entity introduced; no domain transition.
- **Level 2 — component inclusion:** `ProtectionComponent` is world content on mobs → **omit `[Persistent]`**. Its durable form is `MobTemplate` YAML; re-spawn re-applies it. (Note: `MobDataComponent` carries `[Persistent]` but mobs lack `PersistentEntity`, so it is never snapshotted — `ProtectionComponent` correctly omits the attribute to match its world-content domain and the `CurrencyLootComponent` precedent.)
- **Level 3 — save-on-change:** no handler or command calls `SaveEntityAsync`. `setmob protection` writes the YAML template via `IMobContentWriter` (content authoring, not entity persistence) — no INV-22 violation.

## Flows introduced or modified

- **Flow 17 — Combat journey (initiation · round pulse · flee)** (`flow-17-kill-mob-combat-initiation.md`): add the `CanBeAttacked` guard to the initiation leg (step before `TryEnterState`/`StartCombat`); document the `Untargetable` refusal branch.
- **Flow 24 — Abilities journey** (`flow-24-ability-activation.md`): add the `CanBeAttacked` guard to the offensive-opens-combat target-resolution leg.
- **Flow 21 — Effects journey (apply · tick · expire)** (`flow-21-effect-tick.md`): add the `EffectImmune` immune-result branch to the apply leg.
- No **new** flow file (the gates are guard branches on existing journeys, not a new recurring chain). The slice PR must update these three flow files (INV-17 merge gate).

## Test plan / Verification

Derived from Postconditions + Main flow per [`../architecture/07-testing.md`](../architecture/07-testing.md).

- **System-unit — `CombatSystem.CanBeAttacked`:** returns `false` for an entity with `Untargetable`; `true` for `EffectImmune`-only; `true` for no component. (Decision of the new method.)
- **System-unit — `EffectSystem.Apply` immune path:** with `EffectImmune` set, a **harmful** definition returns the immune result and adds no `Effect`/`EffectsComponent`; a **beneficial** definition (positive `BaseMagnitude`) also returns immune (asserts brief decision 2). Without the flag, `Apply` behaves unchanged (regression guard).
- **Handler/flow — Gate A refusal:** a flow-tier test driving `KillCommand` against an `Untargetable` mob asserts no `CombatStateComponent` attached, no state transition, **no `CombatStartedEvent` published**, and a refusal message written (invisible-state postconditions).
- **Handler/flow — Gate B refusal:** driving `AffectCommand` against an `EffectImmune` mob asserts **no `EffectAppliedEvent` published** and the immune message (invisible-state postcondition).
- **System-unit — `MobBuilderSystem.SetMobProtection`:** dual-writes live `ProtectionComponent` flags and `MobTemplate.Protection` (mirrors existing `SetMobType` test in `MobBuilderSystemTests`).
- **Persistence/round-trip (Tier 4) — `MobTemplate.Protection`:** write→YAML→read preserves the flag set; `None`/absent yields no component on `Apply`; unknown flag name is skipped. Models `MobCurrencyLootRoundTripTests` (the non-persistent world-content round-trip precedent — note: this is a *template YAML* round-trip, not a SQLite save/load, because the component is non-`[Persistent]`).
- **Architecture-guard:** `ProtectionComponent` is **not** `[Persistent]` — add it to the `World_content_components_are_not_persistent` attribute guard (which today covers `RoomComponent`/`AreaComponent`). Its non-persistence is *also* independently proven by the Tier-4 template round-trip above; note that the `CurrencyLootComponent` precedent uses that save/load round-trip, **not** the static guard, so cite the round-trip — not "alongside `CurrencyLootComponent`" in the guard.
- **Skipped:** exact refusal/immune prose (presentation); `MobEditor.razor` binding (thin UI plumbing, covered by the catalog round-trip); `ProtectionFlags` enum values (pure data); the `setmob` argument-parse plumbing beyond the system-level dual-write assertion.
- **Testability:** no new randomness/wall-clock/I-O seam introduced; both gates are pure synchronous reads. No INV-26 gap.

## Architecture brief

*In-flight; trimmed on ship.*

### Seams and their homes

| New state / signal | Home (layer) | Notes |
|---|---|---|
| protection flags | `ProtectionComponent` (`[Flags]` enum: `None`, `Untargetable`, `EffectImmune`), cross-cutting component | authored on mobs; **not** `[Persistent]` (world content, INV-23 — durable form is `MobTemplate` YAML) |
| attack gate | combat-domain check (e.g. `ICombatSystem.CanBeAttacked(targetId)`) | read by `KillCommand` **and** the ability-targeting pipeline (≥2 sites → one shared query, not duplicated) |
| effect gate | `IEffectSystem.Apply` immune-result path (core reads the component directly — fine) | returns a structured "not applied / immune" result; callers surface it |
| authoring | `IMobBuilderSystem.SetMobProtection` + `SetmobCommand protection` | dual-write live entity + `MobTemplate`; YAML + Blazor `MobEditor` row |

### The family test (forward generalization)

The general primitive is **invulnerability / immunity** on an entity. Siblings: safe-zone/PvP-safe players, room `safe` flags, sanctuary effects, boss crowd-control immunity. **Build now:** the two-axis `[Flags]` enum. **Shape for later:** *category-granular* effect immunity (immune to `Curse` only, etc.) — the effect system already notes "immunity keys off `Category`"; a per-`EffectCategory` mask replaces the single bool at the same gate sites → **Defer** to [`backlog.md`](../roadmap/backlog.md) (added).

### Observers & contributors

No events, no contributor port. Each refusal is a command/apply-time failure surfaced to the actor as a message — no past-tense fact and no aggregation. (If a future audit/anti-grief surface wants "attack-on-protected refused" telemetry, an event is an additive change then.)

### Ordering & timing

None. The gates are synchronous reads inside existing resolution paths; no heartbeat work, no inter-handler ordering.

### Invariants in tension

- **INV-23:** `ProtectionComponent` is world content on mobs — never `[Persistent]`; re-spawns from `MobTemplate`. A round-trip test asserts it is absent after save/load (the `CurrencyLootComponent` precedent).
- **INV-4:** detection is `HasComponent`/flag read, never `entity is Shopkeeper`.
- **INV-8:** the gate decision lives in the combat system / effect system, not in command bodies (commands surface the result).
- **INV-19:** the combat `CanBeAttacked` check has ≥2 consumers (kill + ability targeting) — land it as one shared query, not two inline copies.
- **INV-18 / INV-25:** authoring tooling + gate tests (combat refusal, effect-immune `Apply`, both beneficial and harmful) ship in-slice.

### Resolved decisions (do not relitigate)

1. **Two-axis `[Flags]`** (`Untargetable` + `EffectImmune`), not a single bool; category-granular deferred.
2. **Effect immunity blocks beneficial and harmful effects alike.**
3. **Refuse at initiation with a message**, not a no-op resolution.
4. **Component is cross-cutting and non-persistent** (mob world content).

## Open questions

- **Future mob-aggro interaction.** When mob aggro/AI lands, aggro selection must skip `Untargetable` mobs. Out of scope here (no aggro exists yet) — note the obligation for the aggro slice; this slice adds no aggro code.
- **Player applicability.** The component is entity-general; this slice authors it on mobs only. Confirm no player-facing authoring is wanted now (PvP-safe is a later, separate concern).
