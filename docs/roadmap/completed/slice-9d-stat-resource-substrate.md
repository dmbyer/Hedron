# Phase 3 Slice 9-d — Stat & Resource Substrate

**PR:** #99 (code) · #98 (design model + 9-d/9-e specs) · **Spec:** [`../../implementation-plans/stat-resource-substrate.md`](../../implementation-plans/stat-resource-substrate.md) · **Gameplay-model spine:** S1

> Ledger backfilled retroactively (the slice merged in #99 without a `done.md`/`completed/` entry at the time).

## Outcome

Replaced the interim three-stat stub (`Strength`/`Dexterity`/`Constitution`, slice 8a) with the gameplay-model's **four primary attributes — Mind, Body, Spirit, Attunement** — and added the **three new resource pools (Mana, Stamina, Astra)** alongside the existing HP. Introduced a `ScoreId` vocabulary and an `IStatRegistry` enumerating every addressable score, and generalized `IStatSystem` into a `ScoreId`-addressable read seam. This is the substrate every later spine writes to (effects target a `ScoreId`; abilities cost a `ResourceType` and are governed by an attribute).

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `AttributesComponent` (Mind/Body/Spirit/Attunement + vestigial `Level`) | `Core/ECS/Components/` | `Str/Dex/Con` removed |
| `PoolsComponent` (HP/Mana/Stamina/Astra, current+max) | `Core/ECS/Components/` | extended from HP-only |
| `ResourceType` enum (`Hp`/`Mana`/`Stamina`/`Astra`) | `Core/ECS/Components/` | expandable seam (R3) |
| `ScoreId` + `IStatRegistry`/`StatRegistry` | `Core/Modules/Stats/` | role + governing-attribute metadata |
| `IAttributeSystem`/`AttributeSystem` | `Core/Modules/Attributes/` | getters/setters for 4 attrs + 4 pools, `[0,max]` clamps |
| `IStatSystem`/`StatSystem` | `Core/Modules/Stats/` | typed getters + generalized `Get(entityId, ScoreId)`; `AttackPower=Body/2+wpn`, `Defense=Body/4` (interim) |
| `CharacterDefaultsOptions` + `CharacterDefaults:` config | `Core/Modules/Account/`, `appsettings.json` | starting attrs 10; HP 100; Mana/Stamina 50; Astra 10 |
| `CombatSystem` (Dex→Body for hit/defense) | `Core/Modules/Combat/` | behavior otherwise unchanged |
| `score`/`setplayer`/`setmob` + `MobTemplate`/builder/writer | various | render/set/round-trip the new model |

## Notable design points

- **`ScoreId` is the S2 hook** — building the `Get(entityId, ScoreId)` seam here let the Effect slice (9-e) fold `StatModifier` summation *inside* `StatSystem` with no consumer churn.
- **Pools are stored, not derived** — Mana/Stamina/Astra maxima are base values; Mind/Body/Attunement governance is recorded in `IStatRegistry` but not yet applied as a formula (a progression/effect concern).
- **`Defense` governance is interim** (`Body/4`) — flagged for a later evasion/armor score. **`Level`** is left in place but inert, to be superseded by the Ascension tier scalar.
- **Persistence: dev-data reset, no migration shim** — `System.Text.Json` ignores the removed `Str/Dex/Con` keys and defaults the new attributes.
- **Starting defaults are Category-3 balance surfaced as config** (the documented OD-2 promotion trigger); end-state promotes to an authored content definition once the content editor lands.

## Deviations from the use-case doc

None — shipped per the WP-1/WP-2/WP-3 plan.

## Follow-ups unlocked

- **9-e (effect substrate):** `StatModifier` effects target a `ScoreId`; the `Get` seam folds them in.
- **10 (death/respawn):** the four pools + `IAttributeSystem` setters power the 25% respawn restore; the `SetCurrentHp` clamp is lowered here's successor.
- **11-a/b/c (abilities, regen):** ability costs draw a `ResourceType`; regeneration writes the pools through `IAttributeSystem`.
