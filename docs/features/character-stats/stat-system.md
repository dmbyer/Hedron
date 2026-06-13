# Stat System

> The generalized read seam for every number on a living entity — primary attributes, resource pools, and derived combat scores — aggregating base values, equipment bonuses, and active effect modifiers into a single `Get(entityId, ScoreId)` call. **Authoring checkpoint:** slice 9-d (stat & resource substrate). Living document — updated by each spine slice (S2 effects, S3 aspects, S4 abilities, S6 progression, S8 ascension).

## What it is / does

`StatSystem` is the **aggregation seam** for effective entity stats. It reads base values from `IAttributeSystem`, equipment bonuses via `EntityService`, and active effect modifiers via `IEffectSystem.GetModifiers`, then produces ready-to-use values for every consumer: the combat round, the `score` command, the prompt pool display, and ability cost checks. It returns results and never publishes events or persists (INV-5). Co-located with `Core/Modules/Stats/`; domain-tier — references other domain systems but never upward (INV-2).

## How it works

### Vocabulary

`ScoreId` (`Core/Modules/Stats/ScoreId.cs`) is an enum identifying every addressable score on an entity:

| Group | Values |
|---|---|
| Primary attributes | `Mind`, `Body`, `Spirit`, `Attunement` |
| Pool max | `HpMax`, `ManaMax`, `StaminaMax`, `AstraMax` |
| Pool current | `HpCurrent`, `ManaCurrent`, `StaminaCurrent`, `AstraCurrent` |
| Derived | `AttackPower`, `Defense` |

`ResourceType` (`Core/ECS/Components/ResourceType.cs`) — enum `{ Hp, Mana, Stamina, Astra }`. Identifies a pool kind without embedding governance or derivation rules. The expandable seam: a future pool is a new entry, not new code.

### Formulas (slice 9-d baseline)

| Score | Formula | Note |
|---|---|---|
| `AttackPower` | `Body / 2 + MainHand.DamageBonus` | weapon slot optional; 0 if no weapon |
| `Defense` | `Body / 4` | interim; dedicated evasion/armor score lands with combat-depth/aspect slices |

Typed getters (`GetEffectiveBody`, etc.) are thin wrappers over `Get(entityId, ScoreId)`. `GetEffectiveAttackPower` reads `EquipmentComponent.Slots[WornSlot.MainHand]` via `EntityService.TryGet<EquipmentComponent>` (direct dictionary lookup, not a list scan) — no `is`/`as` casts (INV-4).

### The effect modifier fold (S2 hook)

The effect substrate (slice 9-e) injected `StatModifier` summation inside `StatSystem.Get` — no interface change for any caller. `Get(entityId, ScoreId)` folds `IEffectSystem.GetModifiers(entityId, scoreId)` on top of base + equipment. Every consumer that calls typed getters receives the buffed/debuffed value transparently. See the [effect-system contributor seam](../effects/effect-system.md#the-contributor-seam).

### Pool governance

`IStatRegistry` (`Core/Modules/Stats/StatRegistry.cs`) enumerates every `ScoreId` with its `ScoreRole` (Primary / Pool / Derived) and governing attribute:

| Pool | Governing attribute |
|---|---|
| HP | none (advances on its own track) |
| Mana | Mind |
| Stamina | Body |
| Astra | Attunement |

Governance is recorded in the registry; pool maxima are stored base values. Derivation from the governing attribute is a progression/effect concern (S6).

### Configuration

Starting defaults for new characters bind from `CharacterDefaults:` in `appsettings.json` via `CharacterDefaultsOptions`. These are Category-3 balance settings surfaced for tuning without recompile (the documented OD-2 trigger); the end-state promotes them to an authored content definition once the content editor exists.

| Key | Default |
|---|---|
| `CharacterDefaults:AttributeDefault` | 10 |
| `CharacterDefaults:MaxHp` | 100 |
| `CharacterDefaults:MaxMana` | 50 |
| `CharacterDefaults:MaxStamina` | 50 |
| `CharacterDefaults:MaxAstra` | 10 |

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IStatSystem.cs`](../../../Core/Modules/Stats/Systems/IStatSystem.cs) — typed effective getters for the four attributes + `GetEffectiveAttackPower` / `GetEffectiveDefense` / `GetCurrentHp` / `GetMaxHp` + the generalized `Get(uint entityId, ScoreId score)` seam. Pure: returns results, never touches the bus or persistence.
- [`IStatRegistry.cs`](../../../Core/Modules/Stats/IStatRegistry.cs) — enumerates each `ScoreId` with its `ScoreRole` and governing attribute.
- [`IAttributeSystem.cs`](../../../Core/Modules/Attributes/Systems/IAttributeSystem.cs) — the raw component read/write layer; see [attribute-system.md](attribute-system.md).

## Considerations

- **`IStatSystem` has no setter methods.** Pure aggregation only. All writes go through `IAttributeSystem` (component write) or `IEquipmentSystem` (slot mutation).
- **`Defense` governance is interim.** `Body / 4` is provisional; a dedicated evasion/armor score and its governing attribute land with the combat-depth/aspect slices — flagged so it isn't mistaken for final.
- **`Level` is vestigial.** Retained in `AttributesComponent` to avoid pulling Ascension (S8) forward; no new feature depends on it.
- **No events in `StatSystem` (INV-5).** Pure computation layer.
- **Acyclic by construction.** `EffectSystem` computes power from base stats (not effective stats), which keeps `Effect → Stat` a DAG — no circular read path through `Get`.

## Extensibility

- **New modifier sources** extend `StatSystem.Get` via `IEffectContributor` (the INV-24 seam) — see the [effect-system contributor seam](../effects/effect-system.md#the-contributor-seam).
- **Armor defense contribution** extends `GetEffectiveDefense` inline when equipment slots carry defense ratings — interface unchanged.
- **Pools-as-derived-scores** (from governing attributes) extend `Get` for max-pool `ScoreId`s — the `IStatRegistry` governance metadata is already in place.
- **Progression / Ascension (S6/S8)** advances scores via `IAttributeSystem` setters; `StatSystem` reads the updated base transparently.

## Related

- [`character-stats.md`](character-stats.md) — the holistic feature view: attributes, pools, derived scores, and the `score` command.
- [`attribute-system.md`](attribute-system.md) — the raw component read/write layer `StatSystem` builds on.
- [`../effects/effect-system.md`](../effects/effect-system.md) — the `StatModifier` kind and the contributor seam `StatSystem` folds.
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — S1 design (§3 Substrate, R3 resources, R4 attributes).
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `StatSystem` / `AttributesComponent` / `PoolsComponent` catalog rows.
- [`../../roadmap/completed/slice-9c-stat-system.md`](../../roadmap/completed/slice-9c-stat-system.md) · [`../../roadmap/completed/slice-9d-stat-resource-substrate.md`](../../roadmap/completed/slice-9d-stat-resource-substrate.md) — the as-built records and decision history.
