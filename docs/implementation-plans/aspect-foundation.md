# Aspect Foundation + Registry Layer (Spine A + Spine F)

- **Status:** implemented
- **Actors:** Player (combatant whose damage/resistance becomes aspect-typed), Administrator (`defs` registry inspection), System (startup referential-integrity validation; combat tick consuming aspect resolution)
- **Module:** new `Core/Modules/Aspects/` (the aspect feature); cross-cutting registry infrastructure in `Core/Systems/`; retrofits `Core/Modules/Abilities/` and `Core/Modules/Effects/`
- **Description:** Introduce the elemental **Aspect** vocabulary ([gameplay-model Spine A](../design/gameplay-model.md#spine-a--aspect-the-elemental-vocabulary)) — `AspectDefinition` + `AspectRegistry` + a core `IAspectSystem` that resolves an aspect-typed magnitude against a source's affinity and a target's resistance — and make combat damage aspect-typed as the first consumer. The same slice lands **Spine F**, the uniform registry layer ([gameplay-model Spine F](../design/gameplay-model.md#spine-f--registry-layer-the-lookup-spine)): a generic `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base that `AspectRegistry` is born on and that the two already-divergent definition registries (`AbilityRegistry`, `EffectRegistry`) retrofit onto. The registry generic rides in this slice rather than a standalone refactor because the rule-of-three (INV-19) is already met by three divergent hand-rolled registries, and anchoring the extraction to a real fourth consumer (Aspect) pressure-tests the abstraction and ships player-visible value (resistances/affinities start mattering) at the same time.

---

## Design notes

> Durable seam rationale — the non-obvious "why here" that survives trim-on-ship (INV-28).

- **The registry generic is shaped by family nature, not a global preference — `IRegistry<TKey, TDef>` carries two type parameters.** Each trait family keys by the identity that matches what it *is*, and the generic accommodates both:
  - **Fixed, code-owned vocabularies** (Aspect, Score, Resource) → **enum key** (`AspectId`, `ScoreId`, `ResourceType`). Small, closed, changed deliberately by developers, and referenced in C# at compile-checkable call sites. An enum gives compile-time reference safety at near-zero cost, and is faithful to the model, which writes `AspectId Id` as a *typed* field, not `string`.
  - **Open, content-authored, persisted families** (Ability, Effect, and later Objective) → **string key**. The decisive reasons: (1) they are destined to be data-authored (YAML, deferred) and a data file cannot extend a compile-time-closed enum; (2) they are **persisted by reference** — `AbilitiesComponent.Known` is a `List<string>` in player saves, a durable contract that an enum-as-ordinal would make fragile; (3) the namespace is decentralized and grows. The compile-safety forgone is safety rarely spent: engine behavioral code **never names a specific ability** (no `if (abilityId == "kick")` exists — abilities flow polymorphically through the resolver → `IAbilitySystem.Activate` → effects). The few reference sites are *data* (cross-refs, the `StartingAbilities` config list) and are covered by the startup validation pass instead.

  Net: enum payoff scales with how often code names a *specific* element; string payoff scales with how much the family is data / persisted / open. The generic lets each family pick correctly, so `StatRegistry` (`IRegistry<ScoreId, …>`) stops being a "principled exception" — only `ITemplateRegistry` stays out (it is a spawn/instance registry that allocates entities, semantically opposite to a definition registry; INV-12).

- **`AspectId` is an enum, and `AbilityDefinition.Aspect` migrates from its pre-spine `string?` stub to `AspectComposition?`.** Aspect is a fixed elemental vocabulary the resolution function operates *over*, not content flowing *through* it — adding an element is a deliberate code-level design act, and "no `FireSystem`" (the spine invariant) is satisfied because `IAspectSystem.Resolve` never branches per aspect regardless of key type.

- **Affinity is a normalized composition; resistance is an independent dimension (gameplay-model R8).** Elemental identity/affinity is an optional **aspect composition** — `AspectId → weight`, either empty (no affinity), a single aspect (100), or a blend summing to 100 — carried by entities, damage packets, and areas; it types outgoing damage and supplies the attacker's per-aspect boost. **Resistance is a separate, independent per-aspect score**, *not* derived from the composition, because aspects are semantic tags that matter beyond damage typing (the owner's intent: affinity will later drive aspect-unique ability/effect riders, so resistance must stand on its own). Both reuse the built stat pipeline / contributor seam (`IStatSystem` + `IEffectContributor`, INV-24) — base in a component, effects/gear contribute on read — and **neither is materialized or cached** (compute-on-read, gameplay-model §2.3). `AspectDefinition` is shaped to grow aspect-unique riders later; the foundation slice lands typing + affinity + independent resistance only.

- **Registries are pure core-tier lookup — they publish nothing (INV-5 trivially) and hold no game semantics (INV-2).** The generic infra lives in `Core/Systems/`; each concrete registry stays in its feature module. `IAspectSystem` is a **core system** (the `Resolve`/`Affinity`/`Resist` math, no game rules). The startup validation pass runs from an Initiator (startup/hosted service), the only place permitted to drive a closed mechanical sweep (INV-10-shaped).

- **Reload is kept *open* without being *decided*.** Today `AbilityRegistry`/`EffectRegistry` bake rows into a `static readonly` dictionary at type-load — un-reloadable. The generic base holds rows as **instance** data (the pattern `StatRegistry` already uses), so a future `Reload(rows)` for YAML-authored families is additive and does not force the hardcoded-vs-YAML override-order decision now (that is deferred — tracked in backlog).

- **The data/code line is held: this slice adds no behavior-dynamism.** Registries hold *definitions* (declarative variation); kinds and behaviors stay in code (effect *kinds* remain an enum + switch in `EffectSystem`). Spine F makes the data/code line structural, it does not move it toward an everything-is-data engine.

- **Per-aspect resistance is owned by `IAspectSystem.Resist`, not by the `ScoreId`-keyed stat seam.** Resistance is parameterized by `AspectId` (`AspectId × value`), a dimension the flat `ScoreId` enum cannot carry. The two wrong fixes are: (1) exploding `ScoreId` into `FireResist`/`VoidResist`/… rows — which couples the closed score enum to the open-ended aspect set and violates the spine rule that "a new aspect is one registry row, not code"; (2) overloading the `ScoreId`-keyed `IEffectContributor` to smuggle an aspect through. Instead, `IAspectSystem` is the **aggregator for the aspect dimension** (mirroring how `IStatSystem` aggregates the `ScoreId` dimension): `Resist(entityId, aspect)` folds `AspectAffinitiesComponent` base + an aspect-keyed contributor query on read, never materialized (INV-24). This is the durable reason the resistance fold does not live in `StatSystem`.

- **Damage typing enriches the existing combat event, it does not add a new one.** The damage's `AspectComposition` is a property of the strike that already happened, not a separate fact; `CombatRoundEvent` / `AbilityStrikeResolvedEvent` carry it as point-in-time capture (INV-6), matching `CombatEndedEvent.DefenderName`. A separate "damage-typed" event would force every witness to correlate two events for one strike — avoided.

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
- `AbilityDefinition.Aspect` migrated from `string?` to `AspectComposition`; `EffectParams.Aspect` migration is deferred (tracked in backlog).
- The host refuses to boot (fail-fast, full report) on any dangling cross-ref (ability→effect, ability→aspect, `StartingAbilities`→ability) or any non-normalized `AspectComposition`.
- `defs <family> [id]` lists any registry's ids and dumps one definition; it is admin-gated.

## Main flow

1. **Startup — registry population.** Each feature module registers its `DefinitionRegistry` subclass in DI as today; the base loads hardcoded rows into instance storage at construction (no `static` dict). `AspectRegistry` is populated with the authored `AspectDefinition` rows. (flow-01, after DI build, before listener opens.)
2. **Startup — referential-integrity validation.** A startup Initiator (`RegistryValidationBootstrap`, a hosted service ordered after world content is ready) sweeps every registry: for each `AbilityDefinition` it asserts each `Effects` id resolves in `EffectRegistry` and its `Aspect` composition resolves in `AspectRegistry`; it asserts every `StartingAbilities`-config id resolves in `AbilityRegistry`; and it asserts every authored `AspectComposition` is empty or sums to 100. On any failure it logs a full report and throws, aborting boot (fail-fast). On success it publishes nothing (closed mechanical sweep, INV-10).
3. **Combat strike — damage typing.** A combat round (`ExecuteRound`) or ability strike (`ResolveAbilityStrike`) computes a raw magnitude as today, then constructs the outgoing damage as `(magnitude, AspectComposition)`. The composition source is: the ability's migrated `Aspect` for an ability strike, else the attacker's `IAspectSystem.Affinity(attackerId)` (entity identity) for a melee round, else empty.
4. **Combat strike — aspect resolution.** `CombatSystem` calls `IAspectSystem.Resolve(magnitude, composition, attackerId, defenderId)`: for each aspect present in the composition, it applies that fraction of the magnitude through the attacker's affinity-derived boost in that aspect and the defender's independent `Resist(defenderId, aspect)`, sums, and clamps. The returned final magnitude replaces the bare `int` in the existing `SetCurrentHp` mutation.
5. **Combat strike — outcome + event.** `ApplyDamageAndBuildResult` proceeds unchanged on the resolved magnitude (HP mutation, outcome classification). `CombatRoundResult` / `AbilityStrikeResult` carry the `AspectComposition` alongside `DamageDealt`; `CombatTickHandler` / the ability pipeline publish the existing `CombatRoundEvent` / `AbilityStrikeResolvedEvent` **enriched** with the composition (point-in-time capture, INV-6) — no new event.
6. **Resistance / affinity on read.** Whenever `IAspectSystem.Resist(entityId, aspect)` or `Affinity(entityId)` is called, it reads `AspectAffinitiesComponent` base values and folds the aspect-keyed contributions (gear/effects) compute-on-read; nothing is materialized into a stored component (INV-24, §2.3).
7. **Admin inspection — `defs`.** An admin runs `defs aspect` to list all aspect ids, then `defs aspect fire` to dump that definition; `defs ability kick` works identically over `AbilityRegistry`. The command resolves the family name to the matching registry, calls `AllIds` / `TryGet`, and writes a typed inspection message.

## Events fired

- **No new events.** The registry layer is pure lookup and publishes nothing (INV-5, trivially). The validation Initiator is a closed mechanical sweep and publishes nothing (INV-10).
- **Existing events enriched (point-in-time capture, INV-6):** `CombatRoundEvent` and `AbilityStrikeResolvedEvent` gain the resolved `AspectComposition` so output/attribution can render the damage type. This is preferred over a new event: the damage type is a *property of the round/strike that already happened*, not a separate fact, and a separate event would force witnesses to correlate two events for one strike. The composition is captured into the payload at publish time (the attacker's affinity at the moment of the strike), matching the INV-6 point-in-time rule already used for `CombatEndedEvent.DefenderName`.

---

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — Spine A (Aspect), Spine F (Registry layer), the substrate table, and resolved decisions R1–R7.
- [`../design/feature-horizon.md`](../design/feature-horizon.md) — the downstream features (aspect-typed abilities, rarity, objectives, …) that instance these spines.
- [`combat.md`](../features/combat/combat.md) / [`stat-system.md`](stat-system.md) / [`effect-substrate.md`](../features/effects/effects.md) / [`ability-substrate.md`](ability-substrate.md) — built consumers and the registries this slice retrofits.
- [`../architecture/checklist.md`](../architecture/checklist.md) — the authoritative invariants every assertion above cites.
