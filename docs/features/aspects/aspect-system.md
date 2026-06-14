# Aspect System

> Core system: elemental vocabulary, affinity/resistance math, and the generic registry layer. **Authoring checkpoint:** slice 11-d (Spine A + Spine F). Living document.

## What it is / does

The aspects feature delivers two shipped spines.

**Spine A — Aspect vocabulary.** `AspectId` (enum: `Fire`, `Ice`, `Lightning`, `Void`, `Nature`, `Light`) + `AspectDefinition` + `AspectRegistry` define the fixed elemental vocabulary. `IAspectSystem` resolves an aspect-typed magnitude against a source's affinity and a target's resistance — pure math, no game-rule branching per element (no FireSystem). Adding a new element is one registry row and a new enum value; no switch-case, no behavior change (INV-15).

**Spine F — Registry layer.** A generic `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base was extracted from three hand-rolled registries (INV-19 rule-of-three threshold); `AbilityRegistry`, `EffectRegistry`, and `StatRegistry` all subclass it. `AspectRegistry` is the fourth consumer that anchored the extraction. See [`../../reference/systems.md`](../../reference/systems.md) for the `DefinitionRegistry` entry.

## How it works

### The `AspectComposition` model

An `AspectComposition` is a normalized `AspectId → weight` map. Three valid states:
- **Empty** — no aspect typing; damage resolves as raw magnitude.
- **Single** — one `AspectId` with weight 100 (pure element).
- **Blend** — multiple aspects with positive weights summing to 100.

`IsValid` asserts the empty-or-sums-to-100 invariant. `AspectComposition` is used as a property of outgoing damage (not a stored component field): it travels in `CombatRoundResult.AspectComposition` and as `AspectComposition?` on `CombatRoundEvent` / `AbilityStrikeResolvedEvent` (point-in-time capture, INV-6). Non-normalization aborts boot (see Startup validation below).

### Affinity and resistance

**Affinity** is the attacker's outgoing elemental composition — a property of who they are, not just what ability they used. `IAspectSystem.Affinity(entityId)` reads `AspectAffinitiesComponent.AffinityWeights` and folds any contributor modifiers. For a melee round with no ability, the attacker's entity affinity sets the composition; for an ability strike, the ability's `def.Aspect` takes priority.

**Resistance** is an independent per-aspect score `[0, 100]` (100 = full immunity). It is NOT derived from affinity — a character may be Fire-affine (Fire damage is their specialty) but also Fire-resistant (they take less Fire damage from others). `IAspectSystem.Resist(entityId, aspect)` reads `AspectAffinitiesComponent.BaseResistances` and folds contributor modifiers on read; nothing is cached (INV-24).

### Resolution formula

`IAspectSystem.Resolve(magnitude, composition, attackerEntityId, defenderEntityId)` applies per-aspect math across the composition:

```
for each (aspect, weight) in composition:
    portion       = magnitude × weight / 100
    boosted       = portion × (1 + attackerAffinityWeight[aspect] / 100)
    final_portion = boosted × (1 − Resist(defender, aspect) / 100)
sum all final_portions; clamp to [0, int.MaxValue]
```

Empty composition → magnitude returned unchanged. Called in both `CombatSystem.ExecuteRound` (melee affinity) and `ResolveAbilityStrike` (ability `Aspect` field). Pure: no events, no persistence, no domain calls (INV-2, INV-5).

### `AspectAffinitiesComponent`

`[Persistent]` component attached **empty** to every new character by `AccountSystem.CreateCharacterAsync`. Fields:

| Field | Type | Notes |
|---|---|---|
| `AffinityWeights` | `Dictionary<AspectId, int>` | Normalized outgoing composition; empty = no affinity |
| `BaseResistances` | `Dictionary<AspectId, int>` | Independent per-aspect base resistance `[0, 100]` |

Serialized by `AspectId` name (never ordinal) via `JsonStringEnumConverter` (INV-23). Admin `setaffinity`/`setresistance` commands are deferred (tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md)).

### Registry design

`AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>` is **enum-keyed** — the fixed, code-owned elemental vocabulary suits a compile-time-checkable key. Contrast with `AbilityRegistry` and `EffectRegistry` (string-keyed) whose content is open, persisted by reference, and destined for data-authored YAML. The two-type-parameter generic accommodates both without forcing a common interface.

| Registry | Key type | Reason |
|---|---|---|
| `AspectRegistry` | `AspectId` (enum) | Fixed vocabulary; compile-time safety; adding an element is a deliberate code-level act |
| `AbilityRegistry` / `EffectRegistry` | `string` | Open, content-authored families; persisted by reference; namespace is decentralized |
| `StatRegistry` | `ScoreId` (enum) | Fixed pool governance metadata |

`ITemplateRegistry` is excluded: it is a spawn/instance registry (allocates entities), semantically opposite to a definition registry (INV-12).

### Startup validation

`RegistryValidationBootstrap` (hosted service, ordered after `WorldContentBootstrap`) calls `IContentValidator.ValidateRegistry` which sweeps every ability for dangling effect/aspect cross-refs and every `AspectComposition` for non-normalization. Any failure logs a full report and throws, aborting boot (INV-10, fail-fast). The bootstrap publishes nothing — closed mechanical sweep.

### Damage-event enrichment

No new events. `CombatRoundEvent` and `AbilityStrikeResolvedEvent` carry `AspectComposition?` as point-in-time capture (INV-6), matching the `CombatEndedEvent.DefenderName` precedent. The null case (empty composition) means the strike was untyped; a separate event would force every witness to correlate two events for one strike — avoided.

## Interface

- [`IAspectSystem.cs`](../../../Core/Modules/Aspects/Systems/IAspectSystem.cs) — `Resolve` / `Affinity` / `Resist`. Pure: no events, no persistence, no domain references.
- [`AspectRegistry.cs`](../../../Core/Modules/Aspects/AspectRegistry.cs) — `IAspectRegistry : IRegistry<AspectId, AspectDefinition>`; six starter rows; registered via `AddAspectsModule()`.
- [`Core/ECS/Components/AspectAffinitiesComponent.cs`](../../../Core/ECS/Components/AspectAffinitiesComponent.cs) — `[Persistent]` component.

## Considerations

- **Per-aspect resistance is owned by `IAspectSystem.Resist`, not by `ScoreId`.** Resistance is parameterized by `AspectId × value`, a dimension the flat `ScoreId` enum cannot carry. Adding `FireResist`/`VoidResist`/… rows to `ScoreId` would couple the closed score enum to the open-ended aspect set, violating the spine rule that "a new aspect = one registry row, not code". `IAspectSystem` is the aggregator for the aspect dimension, mirroring how `IStatSystem` aggregates the `ScoreId` dimension.
- **Compute-on-read, never cached.** Both `Affinity` and `Resist` fold `AspectAffinitiesComponent` base values plus any contributor modifiers on every call. Nothing is materialized into a stored field (INV-24).
- **`EffectParams.Aspect` migration is deferred.** `string? Aspect` remains on `EffectParams` with a `// TODO migrate` comment; the field is unused for damage typing. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Extensibility

- **New aspect** — add one `AspectId` enum value + one `AspectDefinition` row; the resolution loop handles it with no code change.
- **Aspect-unique riders** — `AspectDefinition` is shaped to grow per-aspect behavior (e.g. Fire abilities deal burning DoT). A future slice adds rider fields to `AspectDefinition` and wires them into `EffectSystem`.
- **Per-entity authoring** — `AspectAffinitiesComponent` attaches empty to all characters; admin `setaffinity`/`setresistance` commands are the deferred authoring path.
- **YAML-authored registries** — `DefinitionRegistry` holds instance rows (reload-shaped); promotion to YAML for string-keyed families is tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md). Enum-keyed registries (Aspect, Stat) remain code-owned by design.

## Related

- [`aspects.md`](aspects.md) — holistic feature view and player-facing surfaces.
- [`../../architecture/flows/flow-17-kill-mob-combat-initiation.md`](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) — combat journey where aspect resolution runs.
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine A (Aspect), Spine F (Registry layer), and decisions R1–R7.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `IAspectSystem` / `AspectRegistry` / `AspectAffinitiesComponent` catalog rows.
- [`../../roadmap/completed/aspect-foundation.md`](../../roadmap/completed/aspect-foundation.md) — as-built record and decision history (slice 11-d).
- **Combat** — [`../combat/combat-system.md`](../combat/combat-system.md) for how `IAspectSystem.Resolve` is called in `ExecuteRound` / `ResolveAbilityStrike`.
- **Registry infrastructure** — [`../../reference/systems.md`](../../reference/systems.md) `DefinitionRegistry / IRegistry` entry; the skill doc in [`.claude/skills/add-core-system/SKILL.md`](../../../.claude/skills/add-core-system/SKILL.md) for the enum-vs-string key rule.
