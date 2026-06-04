# Stat, Score, and Resource System

Authoring checkpoint: **slice 9-d** (stat & resource substrate). Living document — updated by each spine slice (S2 effects, S3 aspects, S4 abilities, S6 progression, S8 ascension).

---

## Overview

Every number attached to a living entity — primary attributes, resource pools, derived combat scores — is readable through a single generalized seam: `IStatSystem.Get(entityId, ScoreId)`. Typed convenience getters are thin wrappers over this seam. The seam is the S2 forward-hook: when the effect substrate lands, it injects `StatModifier` summation *inside* `StatSystem` with no interface change for consumers.

---

## Vocabulary

### ScoreId (`Core/Modules/Stats/ScoreId.cs`)

An enum identifying every addressable score on an entity:

| Group | Values |
|---|---|
| Primary attributes | `Mind`, `Body`, `Spirit`, `Attunement` |
| Pool max | `HpMax`, `ManaMax`, `StaminaMax`, `AstraMax` |
| Pool current | `HpCurrent`, `ManaCurrent`, `StaminaCurrent`, `AstraCurrent` |
| Derived | `AttackPower`, `Defense` |

### ResourceType (`Core/ECS/Components/ResourceType.cs`)

Enum `{ Hp, Mana, Stamina, Astra }` — identifies a pool kind without embedding governance or derivation rules. The expandable seam: a future pool is a new entry, not new code.

---

## Layers

### Components (data only — `Core/ECS/Components/`)

| Component | Fields | Persisted |
|---|---|---|
| `AttributesComponent` | `Level`, `Mind`, `Body`, `Spirit`, `Attunement` (int; defaults 1/10/10/10/10) | yes |
| `PoolsComponent` | `MaxHp`/`CurrentHp`, `MaxMana`/`CurrentMana`, `MaxStamina`/`CurrentStamina`, `MaxAstra`/`CurrentAstra` (int; defaults 100/100/50/50/50/50/10/10) | yes |

`Level` is vestigial — retained to avoid pulling Ascension (S8) forward; no new feature depends on it.

### IAttributeSystem (`Core/Modules/Attributes/Systems/`)

Read/write seam for components. INV-5: no event bus, no persistence.

- Attribute getters/setters: `GetMind`/`SetMind`, `GetBody`/`SetBody`, `GetSpirit`/`SetSpirit`, `GetAttunement`/`SetAttunement`.
- Pool getters/setters for all four pools (current + max). Pool invariants: `SetMaxX` clamps `CurrentX` to new `MaxX`; `SetCurrentX` clamps to `[0, MaxX]`.
- `GetLevel`/`SetLevel`.

### IStatSystem (`Core/Modules/Stats/Systems/`)

Aggregation seam. Reads `IAttributeSystem` and equipment components; produces ready-to-use effective values. INV-5: pure aggregation, no bus, no persistence.

```csharp
int GetEffectiveMind(uint entityId);
int GetEffectiveBody(uint entityId);
int GetEffectiveSpirit(uint entityId);
int GetEffectiveAttunement(uint entityId);
int GetEffectiveAttackPower(uint entityId);  // Body/2 + MainHand DamageBonus
int GetEffectiveDefense(uint entityId);      // Body/4 (interim; evasion/armor lands later)
int GetCurrentHp(uint entityId);
int GetMaxHp(uint entityId);
int Get(uint entityId, ScoreId score);       // generalized seam — S2 hook
```

Typed getters are thin wrappers over `Get`. S2 (effect substrate) will sum `StatModifier` effects inside `Get` with no interface churn for existing callers.

### IStatRegistry (`Core/Modules/Stats/`)

Enumerates every `ScoreId` with its `ScoreRole` (Primary / Pool / Derived) and governing attribute (where applicable):

| Pool | Governing attribute |
|---|---|
| HP | none (advances on its own track) |
| Mana | Mind |
| Stamina | Body |
| Astra | Attunement |

The governance is *recorded* here in this slice; it is not yet applied as a derivation formula. Pool maxima are stored base values; derivation from the governing attribute is a progression/effect concern (S6).

---

## Formulas (slice 9-d baseline)

| Score | Formula | Note |
|---|---|---|
| `AttackPower` | `Body / 2 + MainHand.DamageBonus` | weapon slot optional; 0 if no weapon |
| `Defense` | `Body / 4` | interim; dedicated evasion/armor score lands with combat-depth/aspect slices |

---

## Configuration

Starting defaults for new characters are surfaced as Category-3 balance settings via `IConfiguration` (`CharacterDefaults:` section in `appsettings.json`). Injected into `AccountSystem` at construction time via `CharacterDefaultsOptions`.

| Key | Default |
|---|---|
| `CharacterDefaults:AttributeDefault` | 10 |
| `CharacterDefaults:MaxHp` | 100 |
| `CharacterDefaults:MaxMana` | 50 |
| `CharacterDefaults:MaxStamina` | 50 |
| `CharacterDefaults:MaxAstra` | 10 |
| `CharacterDefaults:StartingAbilities` | `["kick","empower"]` |

The end-state promotes these to an authored content definition (Category 2) once the content editor exists; `CharacterDefaultsOptions` is shaped for cheap migration.

---

## What S2 adds here

The effect substrate (slice 9-e) will inject a `StatModifier` summation step inside `StatSystem.Get`. No interface changes are required — `IStatSystem`, `IAttributeSystem`, and all existing callers are untouched. Consumers that call typed getters (`GetEffectiveBody`, etc.) receive the buffed/debuffed value transparently.

---

## Related

- [`../../../docs/design/gameplay-model.md`](../../design/gameplay-model.md) — S1 design (§3 Substrate, R3 resources, R4 attributes)
- [`../../../docs/use-cases/stat-resource-substrate.md`](../../use-cases/stat-resource-substrate.md) — slice 9-d requirements + implementation detail
- [`../../../docs/use-cases/effect-substrate.md`](../../use-cases/effect-substrate.md) — S2; targets `ScoreId` via `StatModifier`
