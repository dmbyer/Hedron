# Attribute System

> The raw read/write seam for `AttributesComponent` and `PoolsComponent` — base attributes and resource pools for every living entity — and the single owner of each pool's **effective cap**. **Status:** live (slices 8a, 9-c, 9-d; pool-cap seam combat-effective-scores).

## What it is / does

`AttributeSystem` is the **component accessor** for the two foundational stat components. It exposes getters and setters for the four primary attributes (Mind, Body, Spirit, Attunement) and the four resource pools (HP, Mana, Stamina, Astra — current and max), enforcing the pool clamp invariants at the system boundary (below). It never publishes events or touches persistence (INV-5); mutation is the caller's (command's) responsibility for those downstream concerns.

`StatSystem` aggregates over these values to produce effective stats, and delegates its pool reads (`Get(<Pool>Max/<Pool>Current)`) to this system's cap seam so display and writes share one definition. `RegenerationSystem` writes pool changes through this system. `CombatSystem` and ability cost checks read through it.

## How it works

### Components

Both components live under `Core/ECS/Components/` (cross-cutting — so `Combat` and other modules can read them without a domain module dependency):

- `AttributesComponent` — `Level` (vestigial; superseded by Ascension tier S8), `Mind`, `Body`, `Spirit`, `Attunement` (int; all default 10). `[Persistent]`.
- `PoolsComponent` — `MaxHp`/`CurrentHp`, `MaxMana`/`CurrentMana`, `MaxStamina`/`CurrentStamina`, `MaxAstra`/`CurrentAstra` (int; HP defaults 100/100, Mana/Stamina 50/50, Astra 10/10). `[Persistent]`.

### The pool-cap seam (keyed by `ResourceType`)

The rule lives in the system, not at call sites, and is one rule for all four pools:

- **Effective cap** — `GetEffectiveMax(entity, pool)` = stored base max + every active `<Pool>Max` modifier (`IEffectSystem.GetModifiers`: timed effects and contributors — gear, passives, progression, tier), floored at **1** when the base is above 0 (0 otherwise). Computed on read, never stored (INV-24). The stored `MaxX` stays the *base* (`GetBaseMaxX`).
- **Clamped reads** — `GetCurrent(entity, pool)` (and the typed `GetCurrentX`) return `min(stored, cap)`, so a dropped cap (a buff expired, gear came off) is visible at once. A negative stored HP (incapacitated, bleeding out) reads unchanged.
- **Writes** — `SetCurrent` / `SetCurrentX` clamp to `[floor, cap]`: the floor is `Death:HpFloor` for HP and 0 otherwise. `SetMax` / `SetMaxX` write the new **base** and clamp stored current to the new **effective** cap (setting a lower max does not heal).
- **Fills** — `RestoreToFraction(entity, pool, fraction)` stores `floor(cap × fraction)` (`RestoreAllToFraction` does all four pools): spawn/creation fills use 1.0, respawn uses `Death:RespawnPoolPercent`.
- **Reconcile** — `ReconcilePools(entity)` / `ReconcileAllPools()` write `min(stored, cap)` back to storage and never raise a value. [`PoolReconcileTickHandler`](../../../Core/Modules/Attributes/Handlers/PoolReconcileTickHandler.cs) runs it once per heartbeat (priority `Domain + 10`, after every p=20 pool writer) and the sandbox executor once per synthetic tick, so stored overflow never outlives a tick — which is what stops a removed-then-reapplied max bonus from refilling current for free. It publishes nothing and never consults death: it only lowers toward a cap of at least 1.
- **Why clamped reads + a tick, not per-source hooks:** most cap sources are contributors, pulled on read with no "changed" signal by design (INV-24). A central sweep covers every source, current and future, without each one remembering to reconcile.

### Default safety

All getters return safe defaults when the entity lacks the relevant component (Level 1, attributes 10, HP/Mana/Stamina/Astra defaults). This covers the pre-hydration edge case where a character loads before `CharacterHydrationHandler` attaches missing components.

### Pool governance

Governance associations (Mana↔Mind, Stamina↔Body, Astra↔Attunement) are recorded in `IStatRegistry` but not yet applied as derivation formulas. Pool maxima are base values set directly by templates and admin commands. Derivation is a progression concern (S6).

## Interface

- [`IAttributeSystem.cs`](../../../Core/Modules/Attributes/Systems/IAttributeSystem.cs) — getters/setters for all four attributes + four pools + `Level`, and the `ResourceType`-keyed cap seam (`GetEffectiveMax`, `GetCurrent`, `SetCurrent`, `SetMax`, `RestoreToFraction`, `ReconcilePools`, `ReconcileAllPools`). Pure: no events, no persistence (INV-5). Must never depend on `IStatSystem`, which delegates its pool reads here (architecture guard).
- [`AttributesComponent.cs`](../../../Core/ECS/Components/AttributesComponent.cs) — the `[Persistent]` attribute data.
- [`PoolsComponent.cs`](../../../Core/ECS/Components/PoolsComponent.cs) — the `[Persistent]` pool data.

## Considerations

- **All mutations publish no events (INV-5).** Event publication is always the caller's (command's) responsibility — `PlayerAttributeSetByAdminEvent`, `MobPropertySetByAdminEvent`, and `EntityStateChangedEvent` are published by commands after calling setters, never by the system itself.
- **`setplayer` is test tooling.** In production, character stat progression will be driven by level-up events. `SetPlayerCommand` provides a direct override path for testing, protected by `AdminRequirement`.
- **`hp` sets MaxHp, clamps CurrentHp.** Setting `hp <n>` updates the base max and clamps the current to the new effective cap if needed — it does not heal the entity to full.
- **Stored current may exceed stored max.** With a persistent `<Pool>Max` source (progression, a passive, worn gear), a full pool stores a current above the base max. That is legitimate and survives save/load; there is no load-time clamp — reads clamp, and the first reconcile writes the clamp back if the cap on load is lower.
- **Migration guard.** `CharacterHydrationHandler` attaches missing `AttributesComponent` and `PoolsComponent` to existing characters on `WorldContentReadyEvent` without immediately saving (matches the established pattern from `InventoryComponent`).

## Related

- [`character-stats.md`](character-stats.md) — the holistic feature view.
- [`stat-system.md`](stat-system.md) — `StatSystem` aggregates over these values to produce effective stats.
- [`regeneration-system.md`](regeneration-system.md) — writes pool deltas through `IAttributeSystem`'s clamped setters each heartbeat tick.
- [`../../reference/components.md`](../../reference/components.md) — `AttributesComponent` and `PoolsComponent` catalog rows.
- [`../../reference/systems.md`](../../reference/systems.md) — `AttributeSystem` catalog row.
- [`../../roadmap/completed/slice-8a-attributes-and-vitals.md`](../../roadmap/completed/slice-8a-attributes-and-vitals.md) · [`../../roadmap/completed/slice-9d-stat-resource-substrate.md`](../../roadmap/completed/slice-9d-stat-resource-substrate.md) — the as-built records.
