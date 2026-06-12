# Effect System

> The core mechanic that applies, stacks, orders, ticks, and persists **effects** — the parameterized modifiers (buffs, debuffs, DoTs/HoTs, auras, curses) behind most gameplay. **Authoring checkpoint:** slice 9-e (`gameplay-model` Spine C). Living document.

## What it is / does

`EffectSystem` is the **core-tier aggregator** that owns the effect lifecycle: it computes an effect's `Power` at apply time, applies the stacking policy, stores standalone effects, advances `Timed`/`Periodic` effects each tick, and exposes the modifier seam that `IStatSystem` reads. It returns results and never publishes events or persists (INV-5); periodic pool writes and event publication are the [tick handler](#how-it-works)'s job. It is co-located with its feature module (`Core/Modules/Effects/`) but is **core-tier** — it references no domain system (INV-2).

## How it works

### The `Effect` record

An `Effect` is an immutable record applied to a target by a source:

| Field | Type | Purpose |
|---|---|---|
| `EffectId` | `string` | Registry key; `HighestWins` comparison and display |
| `Kind` | `EffectKind` | What the effect *does* (below) |
| `Params` | `EffectParams` | `TargetScore`, `BaseMagnitude`, `Aspect` |
| `Category` | `EffectCategory` | Tag for dispel targeting (`RemoveByCategory(Curse)`) |
| `Power` | `int` | Computed at apply time; the `HighestWins` key and magnitude scalar |
| `Source` | `EffectSource` | The entity that applied it |
| `Group` | `string?` | Composite group ID (deferred — composites land later) |
| `Lifetime` | `EffectLifetime` | Controls storage and tick behaviour |
| `Duration` / `Elapsed` | `float` | Seconds until expiry (`-1f` == `UntilRemoved`); `Elapsed` ticks up for `Timed` |
| `Stacking` | `StackPolicy` | How re-application interacts with existing instances |
| `Phase` | `EffectPhase` | Intra-tick ordering (`Early` < `Normal` < `Late`) |

Standalone effects live in `EffectsComponent { List<Effect> Effects }` on the target — a single list; `Lifetime` alone decides persistence (no Persistent/Transient split).

### Kinds

| `EffectKind` | Behaviour | Wired? |
|---|---|---|
| `StatModifier` | Adds signed `Power` to a `ScoreId`, read via `GetModifiers` | yes |
| `Instant` | One-shot magnitude at apply time; not stored | yes |
| `Periodic` | `Power` applied to a pool each heartbeat tick | yes |
| `GrantFlag` | Tag presence; `GetActive` is the query | yes |
| `GrantAbility` | Grants an ability while active | deferred (abilities S4) |
| `Trigger` | Fires a world event on tick/expiry | deferred (world events S5) |
| `TransformModifier` | Modifies generation parameters | deferred (generation S5) |

Deferred kinds are enum values with no handler yet; adding a handler later is additive (no model change).

### Lifetimes & persistence

| `EffectLifetime` | Persisted? | Notes |
|---|---|---|
| `Instant` | No | Never stored; `Apply` returns a synthetic record |
| `Timed` | No | Expires via `AdvanceTick`; dropped on restart mid-duration |
| `UntilRemoved` | **Yes** | Survives restarts; removed only by explicit `Remove`/`RemoveByCategory` |
| `WhileEquipped` / `WhileKnown` / `WhilePresent` | No | Source-bound; re-derived from the source on load, never stored |

**Persistence contract.** `EffectsComponent` is `[Persistent]`; the `[JsonConverter(typeof(EffectsComponentJsonConverter))]` attribute makes the serializer write **only** `UntilRemoved` entries automatically (no `ComponentSerializer` change). Source-bound entries re-derive when their sources load.

### Stacking, Power, and Phase

- **Stacking** (`StackPolicy`): `Stack` (add an instance) · `HighestWins` (keep the strongest per `EffectId`; weaker re-apply ignored) · `Refresh` (reset `Elapsed`) · `UniquePerSource` (one per source) · `Replace` (replace all of this `EffectId`). `HighestWins` is canonical for auras and stat buffs/debuffs where magnitude is rank.
- **Power** is one integer computed at apply time from the **source's base stats** (read via `EntityService` directly — *never* `IStatSystem`, which would create an `Effect↔Stat` cycle). It is simultaneously the `HighestWins` key, the magnitude for `Instant`/`Periodic`, and the signed `StatModifier` delta. `PowerScaling` is a small named-formula registry (`fixed` → `BaseMagnitude`; `byAttunement` → `BaseMagnitude + source.Attunement/5`); provisional per `gameplay-model` §6, `Tier`-scaling lands with Ascension (S8).
- **Phase** (`EffectPhase`) orders intra-tick application: `Early` (HoT/regen) < `Normal` < `Late` (DoT/poison). `AdvanceTick` sorts due applications and expiries by phase, guaranteeing heal-before-damage so an entity isn't killed by a DoT before a HoT that would have saved it.

### The tick (orchestration)

`EffectTickHandler` (priority 20, before `CombatTickHandler`) subscribes to `HeartbeatTickEvent`, calls `AdvanceTick`, writes each due periodic magnitude through `IAttributeSystem` in phase order, routes `HpCurrent` writes through `IDeathSystem.OnHpChanged` (so a DoT can open the incapacitation lifecycle), and publishes `EffectExpiredEvent` per expiry. The full trace is the [effects journey](../../architecture/flows/flow-21-effect-tick.md).

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IEffectSystem.cs`](../../../Core/Modules/Effects/Systems/IEffectSystem.cs) — `Apply` / `Remove` / `RemoveByCategory` / `GetActive` / `GetModifiers(entityId, scoreId)` / `AdvanceTick(elapsed)`. Pure: returns results, never touches the bus or persistence, never calls a domain system.
- [`EffectsComponent.cs`](../../../Core/ECS/Components/EffectsComponent.cs) — the `[Persistent]` store + its lifetime-filtering JSON converter.

## The contributor seam

> Computed-score aggregation from heterogeneous, domain-owned sources. The rule is [INV-24](../../architecture/checklist.md); this is its worked instance and the third [layer-composition shape](../../architecture/01-layers.md#the-three-composition-shapes).

`EffectSystem` is the **aggregator** for everything that modifies a score. `GetModifiers` returns the base-independent modifiers `IStatSystem.Get` folds on top of base + equipment. Stored effects are one source — but modifiers also come from **source-bound** origins that are *derived on read, never stored* (passive abilities; later equipment, auras, areas), owned by domain modules `EffectSystem` (core) may not reference (INV-2).

The seam is **dependency inversion**: a core-owned port `IEffectContributor`, DI-collected as `IEnumerable<IEffectContributor>`. `GetModifiers`/`GetActive` sum the stored effects **plus** every contributor's output; each source ships an adapter in its own module and registers it, so `EffectSystem` stays closed for modification as sources are added.

| Contributor | Source | Lands |
|---|---|---|
| (stored effects) | `EffectsComponent` | 9-e |
| `AbilityEffectContributor` | known `WhileKnown` passive abilities | 11-a |
| equipment / aura / area | worn items, present auras/areas | future, same port |

**Why pull, not push.** Materializing derived modifiers into a stored component caches derived state and reintroduces the "did I recompute when the source changed?" bug family that compute-on-read exists to kill. Contributors **pull** at read time — one source of truth, nothing to invalidate.

## Considerations

- **Acyclic by construction.** Power from *base* (not effective) stats keeps `Effect → Stat` a DAG; periodic magnitudes are written by the handler, not the system.
- **`[power]` is a testing override (INV-8).** The optional `[power]` arg on `affect` forces a value for testing; the sanctioned path (abilities, potions) always lets `Apply` compute Power from `PowerScaling`.
- **Aspect carried, not resolved.** `Params.Aspect` rides every effect but applies as raw magnitude until aspect math (S3).
- **Determinism.** No randomness in the substrate; if a future formula rolls, route it through `IRandom` (INV-26).

## Extensibility

- **New modifier sources** fold in through the `IEffectContributor` port with no `EffectSystem` change (the open/closed property above).
- **Deferred kinds** (`GrantAbility`/`Trigger`/`TransformModifier`) add a handler when their consuming spine lands — additive, no model change.
- **Cross-source resolution** (caps, diminishing returns, `HighestWins` spanning stored **and** contributed instances — resist caps, control-effect DR) is a bounded, additive change: the port passes *contributions with metadata* (`Power`, `StackPolicy`, `Category`) so the aggregator resolves instead of summing. Not built until a consumer needs it.
- **Composite effects** (`CompositeEffectDefinition` — one name → several effects sharing a `Group`) land with the content slice that authors curses/blessings.
- **`Speed` targeting** (`haste`) lands when a combat/initiative slice makes `Speed` a consumed derived score.
- **Effect catalog promotion.** `EffectRegistry` is a hardcoded starter set (`empower`, `weaken`, `regen`, `poison`, `minor_curse`); promotion to a data-file catalog is Category-3 balance work tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Related

- [`effects.md`](effects.md) — the holistic feature view + player/admin surfaces.
- [`../../architecture/flows/flow-21-effect-tick.md`](../../architecture/flows/flow-21-effect-tick.md) — the effects journey (apply · tick · expire).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine C, R5 (stacking/Power), R6 (lifetimes); the upstream design.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `EffectSystem` / `EffectsComponent` catalog rows.
- [`../../roadmap/completed/slice-9e-effect-substrate.md`](../../roadmap/completed/slice-9e-effect-substrate.md) — the as-built record and decision history.
- The stat pipeline that folds `GetModifiers` — `IStatSystem` in [`../../reference/systems.md`](../../reference/systems.md) (its system doc lands when `character-stats/` migrates).
