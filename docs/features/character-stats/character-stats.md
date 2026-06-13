# Character Stats

> Every number that describes a living entity: four primary attributes, four resource pools, derived combat scores, and the out-of-combat regeneration that keeps ability costs sustainable. **Status:** live (slices 8a, 9-c, 9-d, 11-c).

## What it is

Character stats are the numeric substrate every other gameplay system writes to or reads from. From a player's seat: `score` shows your attributes and pools; pools drain during combat and ability use; you regenerate between fights (faster while resting). The `setplayer` and `setmob` admin commands let administrators tune values directly during testing.

The substrate is deliberately simple: all values are stored directly (no derivation formulas from attributes to pools yet — that is a progression concern). The readout is accurate and transparent; there is nothing hidden. A player watching the `score` output sees their pools climbing during rest and can feel the difference between idle and resting rates.

## How it works

The feature composes three cooperating layers:

- **`IAttributeSystem`** owns the raw components — reads and writes `AttributesComponent` and `PoolsComponent` with `[0, max]` clamp invariants. It is the write seam that all other layers use when pool values must change. The full model is the [attribute-system design doc](attribute-system.md).

- **`IStatSystem`** is the aggregation seam — reads base attributes and equipment bonuses, folds in active effect modifiers (`StatModifier` kind via `IEffectSystem.GetModifiers`), and exposes ready-to-use effective values via a `ScoreId`-keyed `Get(entityId, ScoreId)` call. Combat, the `score` command, ability cost checks, and the prompt all read through this seam. The full model is the [stat-system design doc](stat-system.md).

- **`IRegenerationSystem`** is the heartbeat sweep — applies per-pool deltas each tick based on entity state (`InCombat` suppresses; `Resting` accelerates; idle is a slower cadence). Writes through `IAttributeSystem`'s clamped setters. Silent by design; the player observes recovery via `score`. The full model is the [regeneration-system design doc](regeneration-system.md).

The `score` command reads through `IStatSystem` and renders the player's current attributes and pools. The prompt reads through `IStatSystem` for each pool pair and refreshes on every output flush.

## Attributes

Four primary attributes on every living entity:

| Attribute | Current role |
|---|---|
| **Mind** | Governs Mana pool (recorded in `IStatRegistry`; derivation is a progression concern) |
| **Body** | `AttackPower = Body/2 + weapon`; `Defense = Body/4` (interim) |
| **Spirit** | No derived score yet — placeholder for future mechanics |
| **Attunement** | Governs Astra pool (recorded in `IStatRegistry`; derivation is a progression concern) |

Default starting value: **10** for all attributes (configurable via `CharacterDefaults:AttributeDefault`). `Level` is also carried on `AttributesComponent` but is vestigial — retained until the Ascension tier (S8) supersedes it.

## Resource pools

Four pools, each with a current and max value:

| Pool | Default max | Governance |
|---|---|---|
| **HP** | 100 | none (advances on its own track) |
| **Mana** | 50 | Mind (recorded; not yet derived) |
| **Stamina** | 50 | Body (recorded; not yet derived) |
| **Astra** | 10 | Attunement (recorded; not yet derived) |

Pool maxima are base values set directly by character creation, mob templates, and admin commands. Deriving maxima from governing attributes is a progression/effect concern (S6). Starting defaults are configurable via the `CharacterDefaults:` `appsettings.json` section.

## Derived scores

| Score | Formula | Note |
|---|---|---|
| `AttackPower` | `Body / 2 + MainHand.DamageBonus` | weapon slot optional; 0 if no weapon |
| `Defense` | `Body / 4` | interim; dedicated evasion/armor score lands later |

These are computed by `IStatSystem.GetEffectiveAttackPower` / `GetEffectiveDefense` and are visible in the `score` output.

## Regeneration

Out-of-combat regeneration applies to all four pools each heartbeat tick. The rate is state-driven: `InCombat` suppresses regeneration entirely; `Resting` regenerates every tick; idle (standing, not in combat) regenerates on a slower cadence (every 3rd tick). `rest`/`stand` are the player commands for the `Resting` state; combat entry clears it automatically. The regen tick rides the shared heartbeat — see [flow-16-heartbeat-tick](../../architecture/flows/flow-16-heartbeat-tick.md).

## Systems

| System | Role |
|---|---|
| [`attribute-system.md`](attribute-system.md) | Raw component read/write: `AttributesComponent`, `PoolsComponent`, clamp invariants |
| [`stat-system.md`](stat-system.md) | Aggregation seam: base + equipment + effect modifiers → effective values; `ScoreId` vocabulary |
| [`regeneration-system.md`](regeneration-system.md) | Heartbeat sweep: state-based pool regen; `rest`/`stand` commands |

## Surfaces

- **Commands** — `score` (player: shows attributes + pools); `setplayer <name> <attr|pool> <n>` (admin: sets attribute or pool on a connected player); `setmob <bp> <attr|pool> <n>` (admin: sets attribute or pool on a mob + round-trips to YAML); `rest` / `stand` / `wake` (player: enter/exit `Resting` state). See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `PlayerAttributeSetByAdminEvent`, `MobPropertySetByAdminEvent` (admin boundary saves); `EntityStateChangedEvent` (rest/stand transitions). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — `AttributesComponent`, `PoolsComponent` (`[Persistent]`, cross-cutting). See [`../../reference/components.md`](../../reference/components.md).

## Related

- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — S1 design (§3 Substrate, R3 resources, R4 attributes); the upstream design this realizes.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-8 (clamp ownership), INV-5 (systems pure).
- [`../../roadmap/completed/slice-8a-attributes-and-vitals.md`](../../roadmap/completed/slice-8a-attributes-and-vitals.md) · [`../../roadmap/completed/slice-9c-stat-system.md`](../../roadmap/completed/slice-9c-stat-system.md) · [`../../roadmap/completed/slice-9d-stat-resource-substrate.md`](../../roadmap/completed/slice-9d-stat-resource-substrate.md) · [`../../roadmap/completed/slice-11c-resource-regeneration.md`](../../roadmap/completed/slice-11c-resource-regeneration.md) — as-built history and decisions.
- **Effects** — [`../effects/effects.md`](../effects/effects.md) — `StatModifier` effects are folded into `IStatSystem.Get`; `Periodic` HP effects (regen/poison spells) write through `IAttributeSystem` independently of baseline regen.
- **Combat** — `IStatSystem.GetEffectiveAttackPower` / `GetEffectiveDefense` are the combat round's stat inputs; `IAttributeSystem.SetCurrentHp` is the HP write seam.
