# Aspect Foundation + Registry Layer (Spine A + Spine F)

- **Status:** planned
- **Actors:** Player (combatant whose damage/resistance becomes aspect-typed), Administrator (`defs` registry inspection), System (startup referential-integrity validation; combat tick consuming aspect resolution)
- **Module:** new `Core/Modules/Aspects/` (the aspect feature); cross-cutting registry infrastructure in `Core/Systems/`; retrofits `Core/Modules/Abilities/` and `Core/Modules/Effects/`
- **Description:** Introduce the elemental **Aspect** vocabulary ([gameplay-model Spine A](../design/gameplay-model.md#spine-a--aspect-the-elemental-vocabulary)) — `AspectDefinition` + `AspectRegistry` + a core `IAspectSystem` that resolves an aspect-typed magnitude against a source's affinity and a target's resistance — and make combat damage aspect-typed as the first consumer. The same slice lands **Spine F**, the uniform registry layer ([gameplay-model Spine F](../design/gameplay-model.md#spine-f--registry-layer-the-lookup-spine)): a generic `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base that `AspectRegistry` is born on and that the two already-divergent definition registries (`AbilityRegistry`, `EffectRegistry`) retrofit onto. The registry generic rides in this slice rather than a standalone refactor because the rule-of-three (INV-19) is already met by three divergent hand-rolled registries, and anchoring the extraction to a real fourth consumer (Aspect) pressure-tests the abstraction and ships player-visible value (resistances/affinities start mattering) at the same time.

---

## Design notes

> Durable seam rationale — the non-obvious "why here" that survives trim-on-ship (INV-D2).

- **The registry generic is shaped by family nature, not a global preference — `IRegistry<TKey, TDef>` carries two type parameters.** Each trait family keys by the identity that matches what it *is*, and the generic accommodates both:
  - **Fixed, code-owned vocabularies** (Aspect, Score, Resource) → **enum key** (`AspectId`, `ScoreId`, `ResourceType`). Small, closed, changed deliberately by developers, and referenced in C# at compile-checkable call sites. An enum gives compile-time reference safety at near-zero cost, and is faithful to the model, which writes `AspectId Id` as a *typed* field, not `string`.
  - **Open, content-authored, persisted families** (Ability, Effect, and later Objective) → **string key**. The decisive reasons: (1) they are destined to be data-authored (YAML, deferred) and a data file cannot extend a compile-time-closed enum; (2) they are **persisted by reference** — `AbilitiesComponent.Known` is a `List<string>` in player saves, a durable contract that an enum-as-ordinal would make fragile; (3) the namespace is decentralized and grows. The compile-safety forgone is safety rarely spent: engine behavioral code **never names a specific ability** (no `if (abilityId == "kick")` exists — abilities flow polymorphically through the resolver → `IAbilitySystem.Activate` → effects). The few reference sites are *data* (cross-refs, the `StartingAbilities` config list) and are covered by the startup validation pass instead.

  Net: enum payoff scales with how often code names a *specific* element; string payoff scales with how much the family is data / persisted / open. The generic lets each family pick correctly, so `StatRegistry` (`IRegistry<ScoreId, …>`) stops being a "principled exception" — only `ITemplateRegistry` stays out (it is a spawn/instance registry that allocates entities, semantically opposite to a definition registry; INV-12).

- **`AspectId` is an enum, and `AbilityDefinition.Aspect` migrates from its pre-spine `string?` stub to `AspectId?`.** Aspect is a fixed elemental vocabulary the resolution function operates *over*, not content flowing *through* it — adding an element is a deliberate code-level design act, and "no `FireSystem`" (the spine invariant) is satisfied because `IAspectSystem.Resolve` never branches per aspect regardless of key type.

- **Affinity is a normalized composition; resistance is an independent dimension (gameplay-model R8).** Elemental identity/affinity is an optional **aspect composition** — `AspectId → weight`, either empty (no affinity), a single aspect (100), or a blend summing to 100 — carried by entities, damage packets, and areas; it types outgoing damage and supplies the attacker's per-aspect boost. **Resistance is a separate, independent per-aspect score**, *not* derived from the composition, because aspects are semantic tags that matter beyond damage typing (the owner's intent: affinity will later drive aspect-unique ability/effect riders, so resistance must stand on its own). Both reuse the built stat pipeline / contributor seam (`IStatSystem` + `IEffectContributor`, INV-24) — base in a component, effects/gear contribute on read — and **neither is materialized or cached** (compute-on-read, gameplay-model §2.3). `AspectDefinition` is shaped to grow aspect-unique riders later; the foundation slice lands typing + affinity + independent resistance only.

- **Registries are pure core-tier lookup — they publish nothing (INV-5 trivially) and hold no game semantics (INV-2).** The generic infra lives in `Core/Systems/`; each concrete registry stays in its feature module. `IAspectSystem` is a **core system** (the `Resolve`/`Affinity`/`Resist` math, no game rules). The startup validation pass runs from an Initiator (startup/hosted service), the only place permitted to drive a closed mechanical sweep (INV-10-shaped).

- **Reload is kept *open* without being *decided*.** Today `AbilityRegistry`/`EffectRegistry` bake rows into a `static readonly` dictionary at type-load — un-reloadable. The generic base holds rows as **instance** data (the pattern `StatRegistry` already uses), so a future `Reload(rows)` for YAML-authored families is additive and does not force the hardcoded-vs-YAML override-order decision now (that is deferred — see Open questions / backlog).

- **The data/code line is held: this slice adds no behavior-dynamism.** Registries hold *definitions* (declarative variation); kinds and behaviors stay in code (effect *kinds* remain an enum + switch in `EffectSystem`). Spine F makes the data/code line structural, it does not move it toward an everything-is-data engine.

- **Per-aspect resistance is owned by `IAspectSystem.Resist`, not by the `ScoreId`-keyed stat seam.** Resistance is parameterized by `AspectId` (`AspectId × value`), a dimension the flat `ScoreId` enum cannot carry. The two wrong fixes are: (1) exploding `ScoreId` into `FireResist`/`VoidResist`/… rows — which couples the closed score enum to the open-ended aspect set and violates the spine rule that "a new aspect is one registry row, not code"; (2) overloading the `ScoreId`-keyed `IEffectContributor` to smuggle an aspect through. Instead, `IAspectSystem` is the **aggregator for the aspect dimension** (mirroring how `IStatSystem` aggregates the `ScoreId` dimension): `Resist(entityId, aspect)` folds `AspectAffinitiesComponent` base + an aspect-keyed contributor query on read, never materialized (INV-24). This is the durable reason the resistance fold does not live in `StatSystem`.

- **Damage typing enriches the existing combat event, it does not add a new one.** The damage's `AspectComposition` is a property of the strike that already happened, not a separate fact; `CombatRoundEvent` / `AbilityStrikeResolvedEvent` carry it as point-in-time capture (INV-6), matching `CombatEndedEvent.DefenderName`. A separate "damage-typed" event would force every witness to correlate two events for one strike — avoided.

---

## Architecture brief

> In-flight; trimmed on ship (INV-D2). Forward-looking seam analysis for the `use-case-planner` to extend — not the implementation plan.

### Seams + recommended homes / layers

| Seam | Home | Layer | Note |
|---|---|---|---|
| `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base | `Core/Systems/` | core-tier infra | dict + `TryGet`/`Get`/`AllIds`/`All`; instance-held rows (reload-shaped); base is the only place lookup plumbing lives |
| `AspectId` (enum), `AspectDefinition` (record), `AspectRegistry` | `Core/Modules/Aspects/` | data + core registry | `AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>` — rows only |
| `IAspectSystem` (`Resolve`/`Affinity`/`Resist`) | `Core/Modules/Aspects/Systems/` | **core system** | generic math, no game semantics; composed by combat |
| Base aspect **composition** (identity/affinity) | component (`AspectAffinitiesComponent`?) | component | normalized `AspectId → weight` (empty/single/blend); types outgoing damage + attacker boost; persisted by `AspectId` name; not cached |
| **Independent** per-aspect resistance | `IStatSystem` + `IEffectContributor` fold | component + core | decoupled from the composition; summed base + effects on read (INV-24); not cached |
| `EffectRegistry`/`AbilityRegistry` retrofit | their modules | unchanged role | become `DefinitionRegistry<string, …>` subclasses; drop hand-rolled plumbing |
| Startup referential-integrity validation | Initiator (startup/hosted service) | initiator | asserts every cross-ref resolves: ability→effect, ability→aspect, `StartingAbilities`→ability; fail-fast vs log is an open question |
| `defs <family> [id]` admin inspector | a command module | initiator (admin-gated) | one generic inspector over any registry — INV-18 tooling for every future family |

### Family disposition (forward generalization)

- **Build now (Gap exposed → framework lands this slice):** `IRegistry<TKey, TDef>` generic + base; `AspectId`/`AspectDefinition`/`AspectRegistry`; `IAspectSystem` (composition resolution); the `AspectComposition` type + base composition component + independent per-aspect resistance; **full aspect-typed combat** (damage carries a composition, `Resolve` applies affinity + resist); startup validation pass (dangling-ref **and** composition-normalization, fail-fast); generic `defs` inspector. Retrofit `AbilityRegistry` + `EffectRegistry` + `StatRegistry` onto the base.
- **Shape for later (Design note; not built this slice):** registries instance-based so a `Reload` is additive; aspect-unique ability/effect riders on `AspectDefinition` (the "aspects are more than damage" hook); aspect/area/Ascension theming consumers beyond combat.
- **Defer (Acknowledged debt → backlog):** the YAML-authored definition pipeline + hardcoded/YAML coexistence + override/reload order for the big families; migrating `ResourceType` from enum to a string-keyed registry if/when pools need data-authored expandability.

### Observers & contribution / event granularity

- The registry layer is pure lookup — **no events, no publishers** (INV-5 satisfied trivially).
- Aspect resolution plugs into combat's existing damage path. The likely change is **enriching the existing damage event payload with the `AspectId`** (point-in-time capture for correct attribution/output, INV-6) rather than adding a new event — confirm during planning. No new contributor port is needed: affinity/resist ride the built `IEffectContributor`/`IStatSystem` seam.

### Ordering & timing

- Where `IAspectSystem.Resolve` slots into the damage computation (relative to defense/mitigation) is a combat-integration ordering question for the planner — `Resolve` is `magnitude × (1 + affinity) × (1 − resist)`, clamped; its position vs. existing mitigation determines the final number.
- The startup validation pass must run **after every registry is populated** — an ordering constraint on the startup sequence (flow-01).

### Invariants in tension (cite IDs)

- **INV-19** — the rule-of-three justification for extracting the generic now; the **Cross-cutting surfaces stressed** section is the structural check.
- **INV-18** — the `defs` inspector + validation pass + authored `AspectRegistry` rows are the content tooling; the **Content tooling impact** section is the check.
- **INV-2 / INV-24** — registries and `IAspectSystem` are core-tier (no domain deps); affinity/resist reuse the contributor seam rather than a new aggregator or a cached field.
- **INV-5 / INV-10** — registries publish nothing; the validation sweep runs from an Initiator.
- **INV-14 / INV-23** — string keys are durable save contracts (`AbilitiesComponent.Known`); enum keys must **not** be persisted as ordinals. Persisted attunement (keyed by `AspectId`) must serialize by stable name, not ordinal — a persistence-representation decision for the planner.
- **INV-15** — the gameplay-model is the documented target; `AspectDefinition`/`IAspectSystem` shapes come from Spine A.
- **INV-16 / INV-D3** — new `IAspectSystem`, `AspectRegistry`, the `IRegistry` generic, and any `AspectAffinitiesComponent` update `reference/systems.md` + `reference/components.md`.
- **INV-20** — the registry pattern + the enum-vs-string key-type rule should land guidance in the relevant `.claude/skills/*` (e.g. `add-component`, `add-core-system`, `add-domain-system`) in the same PR.

### Resolved decisions (do not relitigate)

1. **Breadth:** generic extracted now + `AspectRegistry` as the 4th consumer + retrofit existing registries; standalone refactor rejected (no player value, abstraction shaped against too few cases).
2. **Generic shape:** `IRegistry<TKey, TDef>` two type params + instance-held base (reload-shaped).
3. **Key type:** enum for fixed code-owned vocabularies (Aspect, Score), string for content/persisted/open families (Ability, Effect). `AspectId` = enum.
4. **Authoring:** hardcoded rows this slice; YAML deferred (additive seam; backlog).
5. **Tooling:** startup referential-integrity validation **and** a generic `defs <family>` inspector both ship this slice.
6. **Combat scope (OQ1):** **full** aspect-typed combat — damage carries an `AspectComposition`; `Resolve` applies affinity + independent resist within the combat tick this slice.
7. **Affinity model (OQ2):** affinity/identity is an optional **normalized aspect composition** (empty / single = 100 / blend summing to 100); **resistance is an independent per-aspect dimension**, decoupled from the composition; `AspectDefinition` shaped for aspect-unique riders later (not built now). See gameplay-model **R8**.
8. **Retrofit breadth (OQ3):** Ability + Effect + **Stat** fold onto `IRegistry<TKey,TDef>` (proves both string and enum keys; no definition-registry outliers remain). No `ResourceRegistry` exists to retrofit (`ResourceType` stays an enum — see backlog).
9. **Aspect migration (OQ4):** `AbilityDefinition.Aspect` (`string?`) → an `AspectComposition`, this slice.
10. **Validation (OQ5):** **fail-fast** — refuse to boot on a dangling cross-ref *or* a non-normalized composition, emitting a full report.
11. **Placement (OQ6):** **slice 11-d — Aspect & Registry Foundation (gameplay-model A + F)**, runnable as a parallel branch to Shopping (no shared dependency).

---

## Open questions

> All six original design forks are **resolved** (see Resolved decisions). What remains is planner / implementation-level — none block the spec gate.

1. **Combat damage-path integration (technical).** Exact position of `IAspectSystem.Resolve` relative to existing defense/mitigation in the combat tick, and whether to enrich the existing damage event payload with the `AspectComposition` (point-in-time capture, INV-6) vs. add a new event. Planner details this against the combat flow.
2. **`AspectComposition` representation.** Integer-percent weights enforced to sum to 100 vs. relative weights normalized on read; persisted form keyed by `AspectId` **name, never ordinal** (INV-23). A representation choice for the planner.
3. **Validation pass home.** `Core/Systems/` startup validator invoked from `Server` composition vs. a hosted service — placement only; the fail-fast behavior is decided (OQ5).
4. **Balance knobs (→ backlog).** The affinity→outgoing-boost and resistance curves are tunable numbers, not architecture — they belong to the balance surface ([`../roadmap/backlog.md`](../roadmap/backlog.md) "Balance & tuning surface"), not this slice.
5. **Per-aspect resistance keying (technical, planner recommendation below).** Resistance is per-aspect (`AspectId × value`), but the built contributor seam (`IEffectContributor.GetModifiers(entityId, ScoreId)`) is keyed by the flat `ScoreId` enum, which cannot express an `AspectId` dimension. Surfaced and resolved in the plan: `IAspectSystem.Resist(entityId, aspectId)` owns the per-aspect fold over `AspectAffinitiesComponent` base + an aspect-keyed contributor query — **not** an explosion of `ScoreId` rows (`FireResist`, `VoidResist`, …) and not an overload of the `ScoreId`-keyed `IEffectContributor`. See Design notes and Cross-cutting surfaces.

---

## Preconditions

- Combat is built and operational (slice 9): `ICombatSystem.ExecuteRound` / `ResolveAbilityStrike` compute a bare `int` damage and apply it via `IAttributeSystem.SetCurrentHp`.
- `IStatSystem` exists with the contributor-fold seam (`Get(entityId, ScoreId)` folds `IEffectSystem.GetModifiers`; `IEffectContributor` is DI-collected, INV-24).
- The three definition registries to retrofit exist and are DI-registered: `AbilityRegistry` + `EffectRegistry` (string-keyed, `static readonly` dict) and `StatRegistry` (enum-keyed `ScoreId`, instance `IReadOnlyList`).
- `AbilityDefinition.Aspect` is a pre-spine `string?` stub; `EffectParams.Aspect` is a parallel `string?` stub. Neither is resolved or consumed today.
- The startup sequence (flow-01) populates every registry before the world is assembled and the listener opens.

## Postconditions

- A generic `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base exists in `Core/Systems/`; `AbilityRegistry`, `EffectRegistry`, and `StatRegistry` are subclasses with no hand-rolled lookup plumbing, and all hold rows as **instance** data (reload-shaped). Behavior is unchanged for existing consumers.
- An `AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>` holds the authored aspect vocabulary; `AspectId` is an enum.
- Combat damage is `(int magnitude, AspectComposition)`; `IAspectSystem.Resolve` applies the attacker's affinity boost and the defender's independent per-aspect resistance within the combat tick, producing the final applied magnitude.
- Entities may carry an `AspectAffinitiesComponent` (normalized affinity composition + base per-aspect resistance map); both are summed compute-on-read and never cached.
- `AbilityDefinition.Aspect` and `EffectParams.Aspect` are migrated from `string?` to an `AspectComposition` (the `EffectParams` migration may be deferred — see Implementation plan WP-2 out-of-scope).
- The host refuses to boot (fail-fast, full report) on any dangling cross-ref (ability→effect, ability→aspect, `StartingAbilities`→ability) or any non-normalized `AspectComposition`.
- `defs <family> [id]` lists any registry's ids and dumps one definition; it is admin-gated.

## Main flow

1. **Startup — registry population.** Each feature module registers its `DefinitionRegistry` subclass in DI as today; the base loads hardcoded rows into instance storage at construction (no `static` dict). `AspectRegistry` is populated with the authored `AspectDefinition` rows. (flow-01, after DI build, before listener opens.)
2. **Startup — referential-integrity validation.** A startup Initiator (`RegistryValidationInitiator`, a hosted service ordered after world content is ready) sweeps every registry: for each `AbilityDefinition` it asserts each `Effects` id resolves in `EffectRegistry` and its `Aspect` composition resolves in `AspectRegistry`; it asserts every `StartingAbilities`-config id resolves in `AbilityRegistry`; and it asserts every authored `AspectComposition` is empty or sums to 100. On any failure it logs a full report and throws, aborting boot (fail-fast, OQ5). On success it publishes nothing (closed mechanical sweep, INV-10).
3. **Combat strike — damage typing.** A combat round (`ExecuteRound`) or ability strike (`ResolveAbilityStrike`) computes a raw magnitude as today, then constructs the outgoing damage as `(magnitude, AspectComposition)`. The composition source is: the ability's migrated `Aspect` for an ability strike, else the attacker's `IAspectSystem.Affinity(attackerId)` (entity identity) for a melee round, else empty.
4. **Combat strike — aspect resolution.** `CombatSystem` calls `IAspectSystem.Resolve(magnitude, composition, attackerId, defenderId)`: for each aspect present in the composition, it applies that fraction of the magnitude through the attacker's affinity-derived boost in that aspect and the defender's independent `Resist(defenderId, aspect)`, sums, and clamps. The returned final magnitude replaces the bare `int` in the existing `SetCurrentHp` mutation. (Ordering relative to the existing defense-mitigation step is specified in Design notes / OQ1.)
5. **Combat strike — outcome + event.** `ApplyDamageAndBuildResult` proceeds unchanged on the resolved magnitude (HP mutation, outcome classification). `CombatRoundResult` / `AbilityStrikeResult` carry the `AspectComposition` alongside `DamageDealt`; `CombatTickHandler` / the ability pipeline publish the existing `CombatRoundEvent` / `AbilityStrikeResolvedEvent` **enriched** with the composition (point-in-time capture, INV-6) — no new event.
6. **Resistance / affinity on read.** Whenever `IAspectSystem.Resist(entityId, aspect)` or `Affinity(entityId)` is called, it reads `AspectAffinitiesComponent` base values and folds the aspect-keyed contributions (gear/effects) compute-on-read; nothing is materialized into a stored component (INV-24, §2.3).
7. **Admin inspection — `defs`.** An admin runs `defs aspect` to list all aspect ids, then `defs aspect fire` to dump that definition; `defs ability kick` works identically over `AbilityRegistry`. The command resolves the family name to the matching registry, calls `AllIds` / `TryGet`, and writes a typed inspection message.

## Events fired

- **No new events.** The registry layer is pure lookup and publishes nothing (INV-5, trivially). The validation Initiator is a closed mechanical sweep and publishes nothing (INV-10).
- **Existing events enriched (point-in-time capture, INV-6):** `CombatRoundEvent` and `AbilityStrikeResolvedEvent` gain the resolved `AspectComposition` so output/attribution can render the damage type. This is preferred over a new event: the damage type is a *property of the round/strike that already happened*, not a separate fact, and a separate event would force witnesses to correlate two events for one strike. The composition is captured into the payload at publish time (the attacker's affinity at the moment of the strike), matching the INV-6 point-in-time rule already used for `CombatEndedEvent.DefenderName`.

## Systems / handlers involved

**New (core infra):**
- `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` (`Core/Systems/`) — `TryGet` / `Get` / `AllIds` / `All`; instance-held rows; the only home for lookup plumbing. Pure core, no events, no game semantics (INV-2, INV-5).

**New (Aspect feature, `Core/Modules/Aspects/`):**
- `AspectId` (enum), `AspectCategory` (enum), `AspectDefinition` (record), `AspectComposition` (normalized value type), `AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>` — data + registry.
- `IAspectSystem` / `AspectSystem` (`Core/Modules/Aspects/Systems/`) — **core system**: `Resolve` / `Affinity` / `Resist`; generic math, no game rules; composed by combat (INV-2). Folds the per-aspect resistance contributions on read (INV-24).

**New (tooling):**
- `RegistryValidationInitiator` (hosted service / Initiator) — startup fail-fast referential-integrity sweep (INV-10).
- `defs` command (`DefsCommand`) — generic admin inspector over any registry (`Full` matching, `AdminRequirement`).

**Changed:**
- `AbilityRegistry`, `EffectRegistry` → `DefinitionRegistry<string, …>` subclasses (drop hand-rolled dict + `TryGet`/`AllIds`); `StatRegistry` → `DefinitionRegistry<ScoreId, ScoreRegistration>` (proves the enum key).
- `AbilityDefinition.Aspect`: `string?` → `AspectComposition`. (`EffectParams.Aspect`: `string?` → `AspectComposition` — may defer; see WP-2.)
- `CombatSystem.ExecuteRound` / `ResolveAbilityStrike` / `ApplyDamageAndBuildResult` — thread the `AspectComposition` and call `IAspectSystem.Resolve` before `SetCurrentHp`. `CombatRoundResult` / the ability-strike result record gain the composition.
- `CombatTickHandler` / `AbilityInvocationPipeline` — populate the enriched event payload.
- `AccountSystem.CreateCharacterAsync` — attach `AspectAffinitiesComponent` to new characters (empty affinity, empty resist base) so the substrate is present for inspection/authoring.
- `IStatSystem` — **unchanged for `ScoreId`-keyed scores.** Per-aspect resistance is *not* added to `ScoreId`; it is owned by `IAspectSystem.Resist`. (Documented here to forestall the wrong fix.)

**Reused unchanged:** `EntityService`, `IAttributeSystem.SetCurrentHp`, `IEffectSystem` / `IEffectContributor` (resist contributors register here in shape, queried by `IAspectSystem`), the heartbeat / combat-tick wiring, `AdminAuthorizer` / dispatcher privilege gate.

## Implementation plan — work packages

Four packages, built **strictly sequentially** (WP-1 → WP-2 → WP-3 → WP-4); each leaves the build green and is independently testable. WP-1 is a pure refactor; WP-2 lands the Aspect substrate with **no combat behavior change**; WP-3 wires that substrate into the combat damage path; WP-4 adds the cross-registry tooling. The split between WP-2 and WP-3 is the natural seam — the substrate (types, registry, system, component) stands and is unit-testable on its own, separate from the combat surgery that consumes it. The **primary agent runs `architecture-reviewer` (code mode) across the combined diff** after all four land.

### WP-1 — Generic registry + retrofit (no behavior change)
- **Scope:** Extract `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` into `Core/Systems/`. Retrofit `AbilityRegistry`, `EffectRegistry` (string key) and `StatRegistry` (enum key, `ScoreId`) onto it. Move all rows from `static readonly` dicts to instance storage populated in the subclass constructor (`StatRegistry` already instance-shaped — adapt it to the base contract; add `TryGet`/`AllIds` it currently lacks).
- **Key extraction (abstraction shape, INV-15):** the base derives each row's key via a **subclass-supplied key selector** (`Func<TDef, TKey>` passed to the base constructor), *not* a shared `IHasId<TKey>` interface on the definition. Reason: the three families' key properties don't share a name — `AbilityDefinition`/`EffectDefinition` expose `Id` (string), `AspectDefinition` exposes `Id` (`AspectId`), but `StatRegistry`'s rows are `ScoreRegistration` whose key is `ScoreId` (a shared record that must not be renamed). A selector lets each family point at its own key without churn. Preserve `IStatRegistry.All` on the retrofit (additive — existing stat consumers read it directly).
- **Files:** `Core/Systems/DefinitionRegistry.cs` (new); `Core/Modules/Abilities/AbilityRegistry.cs`, `Core/Modules/Effects/EffectRegistry.cs`, `Core/Modules/Stats/StatRegistry.cs` (changed); DI wiring untouched (same interface registrations).
- **Depends on:** nothing.
- **Out of scope:** any Aspect type; any combat change; YAML/reload (the base is reload-*shaped*, not reload-*built*).
- **Exit criterion:** project builds; `abilities` / `affects` / `score` and combat behave identically to pre-WP-1; the three registries share one base with no per-registry lookup code; `StatRegistry` exposes `TryGet`/`AllIds`.

### WP-2 — Aspect substrate (no combat behavior change)
- **Scope:** the Aspect module and its data + core system, **with no combat wiring**. `AspectId`, `AspectCategory`, `AspectDefinition`, `AspectComposition`, `AspectRegistry` (born on WP-1's base, with authored starter rows). `IAspectSystem` / `AspectSystem` (`Resolve` / `Affinity` / `Resist` — the pure math, unit-testable in isolation). `AspectAffinitiesComponent` (`[Persistent]`, base affinity composition + base per-aspect resist map), attached empty in `AccountSystem.CreateCharacterAsync`. Register the per-aspect resist contributor(s) for the `IAspectSystem.Resist` fold. Migrate `AbilityDefinition.Aspect` `string?` → `AspectComposition` and populate the ability rows' compositions — the field **changes type and carries data, but nothing consumes it for damage yet** (combat stays untyped until WP-3).
- **Files:** `Core/Modules/Aspects/` (new module: `AspectsModule.cs`, `AspectId.cs`, `AspectDefinition.cs`, `AspectComposition.cs`, `AspectRegistry.cs`, `Systems/IAspectSystem.cs`, `Systems/AspectSystem.cs`); `Core/ECS/Components/AspectAffinitiesComponent.cs` (new, cross-cutting); `Core/Modules/Abilities/AbilityDefinition.cs`, `AbilityRegistry.cs` (changed — `Aspect` type + rows); `Core/Modules/Account/Systems/AccountSystem.cs` (changed — attach component); `Server/Program.cs` (register `AddAspectsModule`).
- **Depends on:** WP-1.
- **Out of scope:** any combat behavior change (WP-3); the `defs` inspector + validation sweep (WP-4); aspect-unique ability/effect riders on `AspectDefinition` (shape-for-later); aspect oppositions (R2: field declared, no pairs); the `EffectParams.Aspect` migration **may be deferred** — if it expands the diff, file a one-line backlog note and leave the `string?` stub with a `// TODO migrate` (do not consume it).
- **Exit criterion:** build green; `AspectRegistry` populated; `IAspectSystem.Resolve`/`Affinity`/`Resist` produce correct values in isolation (e.g. a unit check: 100 dmg of a 100%-Fire composition vs. a 50%-fire-resist target → 50); new characters carry an (empty) `AspectAffinitiesComponent` that round-trips save/load by aspect **name** (not ordinal); **combat behavior is unchanged** — the migrated `Aspect` field is carried but not yet read by the damage path.

### WP-3 — Combat integration (aspect-typed damage)
- **Scope:** consume WP-2's substrate in the combat tick. Thread the `AspectComposition` through `CombatSystem.ExecuteRound` / `ResolveAbilityStrike` / `ApplyDamageAndBuildResult`: construct the outgoing damage as `(magnitude, AspectComposition)` (composition source: the ability's migrated `Aspect` for an ability strike, else the attacker's `IAspectSystem.Affinity(attackerId)` for a melee round, else empty), call `IAspectSystem.Resolve` to apply affinity + independent resist, and use the resolved magnitude in the existing `SetCurrentHp` mutation (ordering vs. existing defense mitigation per OQ1). Enrich `CombatRoundResult` + the ability-strike result + their events (`CombatRoundEvent` / `AbilityStrikeResolvedEvent`) with the composition; `CombatTickHandler` / `AbilityInvocationPipeline` populate the payload.
- **Files:** `Core/Modules/Combat/Systems/CombatSystem.cs`, `Core/Modules/Combat/Events/CombatRoundEvent.cs`, the ability-strike event, `CombatTickHandler.cs`, `AbilityInvocationPipeline` (changed); flow doc updates (flow-18, and the strike-path step text in flow-24/26).
- **Depends on:** WP-2.
- **Out of scope:** the `defs` inspector + validation sweep (WP-4); any `IStatSystem` change (per-aspect resistance is owned by `IAspectSystem.Resist`, never added to `ScoreId`); aspect riders (shape-for-later).
- **Exit criterion:** an aspect-typed strike (a fire-typed ability vs. a fire-resistant target) deals visibly reduced damage vs. an un-resisted target; a melee round with an attacker carrying an affinity composition deals correspondingly boosted damage; `score` / combat output renders the damage type; flow-18 updated.

### WP-4 — Tooling (validation + `defs`)
- **Scope:** `RegistryValidationInitiator` (fail-fast sweep: dangling ability→effect / ability→aspect / `StartingAbilities`→ability refs + composition normalization, full report + throw). Generic `defs <family> [id]` admin inspector mapping a family name to the matching registry's `AllIds`/`TryGet` and rendering a typed message.
- **Files:** `Core/Systems/RegistryValidation.cs` or `Core/Modules/Aspects/RegistryValidationInitiator.cs` (placement OQ3 — recommend `Core/Systems/` as a cross-registry concern, invoked as a hosted service from `Server/Program.cs` ordered after `WorldContentReadyEvent`); `Core/Modules/Admin/Commands/DefsCommand.cs` (new) + its output message; `Server/Program.cs` (register both).
- **Depends on:** WP-2 (needs `AspectRegistry` + the migrated `Aspect` fields to validate/inspect; independent of WP-3's combat wiring, but sequenced after it per the linear build order).
- **Out of scope:** YAML authoring of definitions; per-family bespoke inspectors (one generic inspector only).
- **Exit criterion:** introducing a deliberately dangling cross-ref or a composition summing to 90 aborts boot with a full report; removing it boots clean; `defs aspect`, `defs aspect <id>`, and `defs ability <id>` all return correct output and are rejected for non-admins.

## Content tooling impact

INV-18 satisfied within the slice:
- **Authored data:** the hardcoded `AspectRegistry` rows (the starter aspect vocabulary) and the migrated `Aspect` composition on `AbilityRegistry` rows. YAML authoring is explicitly deferred (additive seam; backlog) — definitions are code rows this slice.
- **Inspection:** `defs <family> [id]` is the new inspector and is generic over *every* registry — it is the durable INV-18 tooling for all current and future definition families (the new gameplay state — aspect definitions — is inspectable in the same PR that introduces it).
- **Validation:** the startup fail-fast referential-integrity pass is authoring-time safety — a designer who writes a dangling cross-ref or a malformed composition learns at boot, not at runtime.
- **`AspectAffinitiesComponent` authoring:** base affinity/resist is attached empty on character creation this slice; an admin editing command for per-entity affinity is out of scope (no player-facing authoring of affinity yet — only the substrate + global definitions). Acknowledged: when entity-level affinity becomes designer-tunable, a `setaffinity`-style admin command lands with that slice. Noted, not a gap for *this* slice (no gameplay path sets non-empty affinity yet beyond ability typing).

## Cross-cutting surfaces stressed

- **Registry pattern (the INV-19 framework itself):** **Gap exposed → framework lands this slice.** Three divergent hand-rolled definition registries already exist (rule-of-three met); `AspectRegistry` would be the fourth. The generic `IRegistry<TKey, TDef>` + `DefinitionRegistry` base is the framework, landed in WP-1 with the three retrofits, anchored to the real fourth consumer in WP-2. This is the structural resolution, not deferred debt.
- **Combat / damage path:** **Gap exposed → resolved in-slice.** Damage is a bare `int` today; making it `(magnitude, AspectComposition)` and inserting `IAspectSystem.Resolve` is a real change to the combat tick, specified in WP-3 and the combat-round flow. Not hand-rolled per aspect — one resolution function, no `FireSystem` (the spine invariant).
- **Per-aspect resistance keying:** **Gap exposed → resolved in-slice (design recommendation).** The built `IEffectContributor` keys by the flat `ScoreId` enum and cannot express `AspectId × value`. Resolution: `IAspectSystem.Resist` owns the per-aspect fold (base map + aspect-keyed contributor query), rather than (a) exploding `ScoreId` into `FireResist`/`VoidResist`/… rows — which couples the score enum to the open-ended aspect set and breaks the spine's "new aspect = one registry row" rule — or (b) overloading the `ScoreId`-keyed `IEffectContributor`. This keeps INV-24's "core aggregator, pull-on-read, no cache" shape with `IAspectSystem` as the aggregator for the aspect dimension.
- **Persistence:** **Adequate, with a representation constraint (INV-23).** `AspectAffinitiesComponent` is `[Persistent]` on a persistent entity (players) — fits the two-domain model. The composition/resist maps **must serialize by `AspectId` name, not ordinal** (enum-as-ordinal is a fragile save contract, INV-23) — enforce via the camelCase `JsonStringEnumConverter` already configured in `ComponentSerializer`, so no new persistence infra. (Note: these are `Dictionary<AspectId, …>` maps — on the .NET 8 target, `System.Text.Json` serializes enum *dictionary keys* as names by default, so the maps round-trip by name; OQ2 owns the final representation.) World content (mobs/areas) that later carry affinity will do so via fresh-spawned components, never `PersistentEntity` (out of scope this slice).
- **Commands / output (`defs`):** **Adequate.** `defs` is a standard admin `ICommand` (`Full` matching, `AdminRequirement`, typed output message) — the built command + output frameworks cover it with no new infrastructure; the dispatcher's privilege gate handles authorization.
- **Event bus:** **Adequate.** No new events; two existing events gain a field (point-in-time capture, INV-6). The bus, handler ordering, and output batching are unchanged.
- **ECS queries:** **Adequate.** `AspectAffinitiesComponent` is read via `TryGet<T>` (INV-4); no type checks.
- **Configuration:** **Adequate.** No new operational config this slice (balance curves are Category-3, deferred to the balance backlog; the validator's fail-fast behavior is decided, not configurable).
- **Initiators / startup ordering:** **Adequate.** The validation Initiator is a closed mechanical sweep (INV-10) ordered after registry population + world content (flow-01) — a hosted-service ordering constraint, not new infrastructure.

## Flows introduced or modified

- **flow-18 (Combat round pulse) — modified.** The strike now constructs `(magnitude, AspectComposition)` and calls `IAspectSystem.Resolve` before `SetCurrentHp`; the published `CombatRoundEvent` carries the composition. WP-3's PR updates flow-18 (body + mermaid: add the `IAspectSystem.Resolve` participant/step). flow-24/26 (ability activation / offensive ability) similarly reference the strike path through `ResolveAbilityStrike` — update the relevant step text where the magnitude is now aspect-resolved.
- **flow-01 (Server startup) — modified.** Adds the registry-population guarantee (instance rows loaded at DI construction) and the new `RegistryValidationInitiator` step (fail-fast sweep after `WorldContentReadyEvent`, before the listener serves). WP-1 (population) + WP-4 (validation) update flow-01 (body + mermaid: add the validation Initiator box ordered after `WorldContentBootstrap`).
- **No new canonical flow.** Aspect resolution is a step inside an existing flow, not a recurring chain of its own; `defs` is a one-shot admin command (described in this doc, not promoted to `flows/`).

## Reference catalog updates

Implemented entries (add when each WP merges, INV-16):
- `reference/systems.md`: `DefinitionRegistry` / `IRegistry<TKey,TDef>` (core infra, WP-1); `AspectRegistry`, `AspectSystem` / `IAspectSystem` (WP-2); `RegistryValidationInitiator` (Background Services / Initiators, WP-4); note the retrofit on the `AbilityRegistry` / `EffectRegistry` / `StatRegistry` entries (now `DefinitionRegistry` subclasses) and on `CombatSystem` (aspect-resolved damage).
- `reference/components.md`: `AspectAffinitiesComponent` (cross-cutting, `[Persistent]`, serialized by `AspectId` name; WP-2); note the `ResourceType`-style co-located `AspectId` / `AspectCategory` enums.
- `reference/commands.md`: `defs` admin command (WP-4).

Planned companions (INV-D3 — until the matching WP merges, the design sits in `*-planned.md`, not the implemented catalog): none required separately — each entry moves straight into the implemented catalog as its WP lands. The pre-existing `systems-planned.md` `SkillSystem`/`EffectTracker` sketches are unaffected by this slice.

---

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — Spine A (Aspect), Spine F (Registry layer), the substrate table, and resolved decisions R1–R7.
- [`../design/feature-horizon.md`](../design/feature-horizon.md) — the downstream features (aspect-typed abilities, rarity, objectives, …) that instance these spines.
- [`combat.md`](combat.md) / [`stat-system.md`](stat-system.md) / [`effect-substrate.md`](effect-substrate.md) / [`ability-substrate.md`](ability-substrate.md) — built consumers and the registries this slice retrofits.
- [`../architecture/checklist.md`](../architecture/checklist.md) — the authoritative invariants every assertion above cites.
