# Attribute System

> The raw read/write seam for `AttributesComponent` and `PoolsComponent` — base attributes and resource pools for every living entity. **Status:** live (slices 8a, 9-c, 9-d).

## What it is / does

`AttributeSystem` is the **component accessor** for the two foundational stat components. It exposes getters and setters for the four primary attributes (Mind, Body, Spirit, Attunement) and the four resource pools (HP, Mana, Stamina, Astra — current and max), enforcing `[0, max]` clamp invariants at the system boundary (INV-8). It never publishes events or touches persistence (INV-5); mutation is the caller's (command's) responsibility for those downstream concerns.

`StatSystem` aggregates over these values to produce effective stats. `RegenerationSystem` writes pool changes through this system. `CombatSystem` and ability cost checks read through it.

## How it works

### Components

Both components live under `Core/ECS/Components/` (cross-cutting — so `Combat` and other modules can read them without a domain module dependency):

- `AttributesComponent` — `Level` (vestigial; superseded by Ascension tier S8), `Mind`, `Body`, `Spirit`, `Attunement` (int; all default 10). `[Persistent]`.
- `PoolsComponent` — `MaxHp`/`CurrentHp`, `MaxMana`/`CurrentMana`, `MaxStamina`/`CurrentStamina`, `MaxAstra`/`CurrentAstra` (int; HP defaults 100/100, Mana/Stamina 50/50, Astra 10/10). `[Persistent]`.

### Clamp invariants (INV-8)

The rule lives in the system, not at call sites:

- `SetMaxX(entityId, value)` — updates the pool max and clamps `CurrentX` to `min(CurrentX, value)`. Setting a lower max does not heal the entity.
- `SetCurrentX(entityId, value)` — clamps `value` to `[0, MaxX]` before writing. Callers pass the raw computed value; the system enforces the bounds.

### Default safety

All getters return safe defaults when the entity lacks the relevant component (Level 1, attributes 10, HP/Mana/Stamina/Astra defaults). This covers the pre-hydration edge case where a character loads before `CharacterHydrationHandler` attaches missing components.

### Pool governance

Governance associations (Mana↔Mind, Stamina↔Body, Astra↔Attunement) are recorded in `IStatRegistry` but not yet applied as derivation formulas. Pool maxima are base values set directly by templates and admin commands. Derivation is a progression concern (S6).

## Interface

- [`IAttributeSystem.cs`](../../../Core/Modules/Attributes/Systems/IAttributeSystem.cs) — getters/setters for all four attributes + four pools (current + max) + `Level`. Pure: no events, no persistence (INV-5).
- [`AttributesComponent.cs`](../../../Core/ECS/Components/AttributesComponent.cs) — the `[Persistent]` attribute data.
- [`PoolsComponent.cs`](../../../Core/ECS/Components/PoolsComponent.cs) — the `[Persistent]` pool data.

## Considerations

- **All mutations publish no events (INV-5).** Event publication is always the caller's (command's) responsibility — `PlayerAttributeSetByAdminEvent`, `MobPropertySetByAdminEvent`, and `EntityStateChangedEvent` are published by commands after calling setters, never by the system itself.
- **`setplayer` is test tooling.** In production, character stat progression will be driven by level-up events. `SetPlayerCommand` provides a direct override path for testing, protected by `AdminRequirement`.
- **`hp` sets MaxHp, clamps CurrentHp.** Setting `hp <n>` updates the max and clamps the current if needed — it does not heal the entity to full.
- **Migration guard.** `CharacterHydrationHandler` attaches missing `AttributesComponent` and `PoolsComponent` to existing characters on `WorldContentReadyEvent` without immediately saving (matches the established pattern from `InventoryComponent`).

## Related

- [`character-stats.md`](character-stats.md) — the holistic feature view.
- [`stat-system.md`](stat-system.md) — `StatSystem` aggregates over these values to produce effective stats.
- [`regeneration-system.md`](regeneration-system.md) — writes pool deltas through `IAttributeSystem`'s clamped setters each heartbeat tick.
- [`../../reference/components.md`](../../reference/components.md) — `AttributesComponent` and `PoolsComponent` catalog rows.
- [`../../reference/systems.md`](../../reference/systems.md) — `AttributeSystem` catalog row.
- [`../../roadmap/completed/slice-8a-attributes-and-vitals.md`](../../roadmap/completed/slice-8a-attributes-and-vitals.md) · [`../../roadmap/completed/slice-9d-stat-resource-substrate.md`](../../roadmap/completed/slice-9d-stat-resource-substrate.md) — the as-built records.
