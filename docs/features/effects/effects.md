# Effects

> Status effects on entities — buffs, debuffs, damage- and heal-over-time, auras, curses, blessings, and poisons. The substrate most other gameplay is built on: abilities produce effects, potions apply them, gear and areas grant them. **Status:** live (slice 9-e; `StatModifier` / `Instant` / `Periodic` / `GrantFlag` wired).

## What it is

An **effect** is something applied to a living entity that changes it — now or over time. A buff that raises an attribute, a poison that drains HP each tick, a heal-over-time, a curse that lingers until cleansed, an aura whose strength outranks a weaker one. From a player's seat: effects are the visible, fading (or permanent) modifiers shown by the `affects` command, applied by abilities and items, and cleared by death, dispel, or expiry.

The same shape covers all of them — each is a parameterized instance of a small fixed set of *kinds* — so a designer authors "poison" and "blessing" as data, not new code. Aspect-typed damage, ability buffs, equipment bonuses, and area auras are all *effects* once their producing feature lands.

## How it works

The feature composes three pieces at the orchestration level:

- **`EffectSystem`** (core) owns the lifecycle — compute power, apply the stacking rule, store, tick, expire — and exposes the modifier seam. It decides; it doesn't broadcast.
- **`EffectTickHandler`** (heartbeat subscriber) drives periodic application and expiry each tick, writes pool changes through `IAttributeSystem`, and publishes the expiry events. Orchestration only.
- **The stat pipeline** reads effect modifiers transparently: `IStatSystem.Get` folds `EffectSystem.GetModifiers` on top of base + equipment, so combat, the `score` command, and every other consumer see buffed/debuffed values with **no call-site change**.

The keystone design is the **contributor seam** ([INV-24](../../architecture/checklist.md)): new modifier sources (passive abilities today; equipment, auras, areas later) plug into the same `GetModifiers` aggregation through a core-owned port, *pulled on read*, so a buff's source stays the single source of truth and nothing has to be invalidated when it changes. The full model — kinds, lifetimes, stacking, power, phase ordering, the persistence contract, and the seam — is the [effect-system design doc](effect-system.md).

## Systems

| System | Role |
|---|---|
| [`effect-system.md`](effect-system.md) | The effect model + lifecycle: apply, stack, power, phase, tick, expire, persist, and the contributor seam |

## Surfaces

- **Commands** — `affect <target> <effectId> [power]` (admin; `[power]` is a testing override) and `affects` (lists active effects: category, power, remaining). See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `EffectAppliedEvent`, `EffectExpiredEvent`, `EffectAppliedByAdminEvent` (thin, past-tense). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Component** — `EffectsComponent` (`[Persistent]`, lifetime-filtered). See [`../../reference/components.md`](../../reference/components.md).

## Flows

- [Effects journey (apply · tick · expire)](../../architecture/flows/flow-21-effect-tick.md) — how an effect is applied, ticked on the heartbeat in phase order, and expired.

## Related

- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine C, the design this realizes.
- [`../../roadmap/completed/slice-9e-effect-substrate.md`](../../roadmap/completed/slice-9e-effect-substrate.md) — as-built history and decisions.
- **Consumers** (cross-feature links added as they migrate): combat reads effect-modified stats and later produces combat effects; abilities and items produce effects via `EffectSystem.Apply`.
