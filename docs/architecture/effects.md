# Effects — design reference

> **Purpose.** This doc captures the effect model design — the decisions and invariants that every future slice consuming effects must understand. The per-slice behavioral spec lives in [`../implementation-plans/effect-substrate.md`](../implementation-plans/effect-substrate.md). See [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine C and decisions R5/R6 for the upstream design inputs.

---

## What an effect is

An `Effect` is an immutable record applied to an entity by a source. It carries:

| Field | Type | Purpose |
|---|---|---|
| `EffectId` | `string` | Registry key; used for `HighestWins` comparison and display |
| `Kind` | `EffectKind` | What the effect *does* (see below) |
| `Params` | `EffectParams` | `TargetScore`, `BaseMagnitude`, `Aspect` |
| `Category` | `EffectCategory` | Tag for dispel targeting (`RemoveByCategory(Curse)`) |
| `Power` | `int` | Computed at apply time; the `HighestWins` key and magnitude scalar |
| `Source` | `EffectSource` | The entity that applied it |
| `Group` | `string?` | Composite group ID (deferred — composites land with S5) |
| `Lifetime` | `EffectLifetime` | Controls storage and tick behaviour |
| `Duration` | `float` | Seconds until expiry; `-1f` == `UntilRemoved` |
| `Elapsed` | `float` | Ticks up each heartbeat for `Timed` effects |
| `Stacking` | `StackPolicy` | How re-application interacts with existing instances |
| `Phase` | `EffectPhase` | Ordering within a tick (Early < Normal < Late) |

Effects live in `EffectsComponent { List<Effect> Effects }` on their target entity.

---

## Kinds

| `EffectKind` | Behaviour | Handler | Deferred? |
|---|---|---|---|
| `StatModifier` | Adds `Power` (signed) to a `ScoreId` via `IEffectSystem.GetModifiers` | `StatSystem.Get` reads the seam on every call | No |
| `Instant` | One-shot magnitude applied at apply time; not stored | `IEffectSystem.Apply` returns result without storing | No |
| `Periodic` | `Power` applied to target pool each heartbeat tick | `EffectTickHandler` via `IAttributeSystem` | No |
| `GrantFlag` | Tag presence; `GetActive` is the query | Check `GetActive` for the flag kind | No |
| `GrantAbility` | Grants an ability while active | Deferred to S4 (abilities) | Yes |
| `Trigger` | Fires a world event on tick/expiry | Deferred to S5 (world events) | Yes |
| `TransformModifier` | Modifies generation parameters | Deferred to S5 (generation) | Yes |

Deferred kinds are defined in the enum; their handlers are additive (no model change when added).

---

## Lifetimes

| `EffectLifetime` | Duration | Persisted? | Notes |
|---|---|---|---|
| `Instant` | 0 | No | Never stored; `Apply` returns synthetic record only |
| `Timed` | `> 0` seconds | No | Expires via `AdvanceTick`; dropped if game restarts mid-duration |
| `UntilRemoved` | `-1f` | **Yes** | Written by `EffectsComponentJsonConverter`; survives restarts |
| `WhileEquipped` | Source-bound | No | Derived from equipment; not stored — re-derives from source on load |
| `WhileKnown` | Source-bound | No | Derived from known abilities (S4, built in slice 11-a) |
| `WhilePresent` | Source-bound | No | Derived from area/aura presence |

**Persistence contract.** `EffectsComponent` is `[Persistent]`. The `[JsonConverter(typeof(EffectsComponentJsonConverter))]` attribute on `EffectsComponent` causes `ComponentSerializer` to use the custom converter automatically — only `UntilRemoved` entries are written. Source-bound entries (`WhileEquipped`, etc.) re-derive when their sources load.

---

## Stacking policies

| `StackPolicy` | Behaviour |
|---|---|
| `Stack` | Every application adds a new instance |
| `HighestWins` | Only the highest-Power instance for a given `EffectId` is kept; a weaker re-apply is ignored |
| `Refresh` | Re-application from the same source resets `Elapsed` to 0; does not add a second instance |
| `UniquePerSource` | One instance per source entity; re-apply replaces |
| `Replace` | All instances of this `EffectId` are replaced by the new one |

`HighestWins` is the canonical policy for auras and stat-modifier buffs/debuffs where magnitude determines rank.

---

## Power

`Power` is a single integer computed at apply time by `PowerScaling.Evaluate(formula, definition, entityService, sourceEntityId)`. It serves as:

1. The `HighestWins` comparison key.
2. The raw magnitude for `Periodic` and `Instant` effects.
3. The signed modifier for `StatModifier` effects (negative Power = debuff).

**Formula registry (`PowerScaling.cs`).**

| Formula key | Result |
|---|---|
| `"fixed"` | `BaseMagnitude` (no stat scaling) |
| `"byAttunement"` | `BaseMagnitude + source.Attunement / 5` |

Power is computed from **source's base stats** (via `EntityService` directly), never from `IStatSystem.Get` (would create an `EffectSystem`↔`StatSystem` cycle). The formula framework is provisional (gameplay-model §6); `Tier`-based scaling lands with Ascension (S8).

---

## Phase ordering

`EffectPhase` controls intra-tick ordering for `Periodic` applications and expiry notifications.

| Phase | Intended use |
|---|---|
| `Early` | HoT (healing-over-time), regen |
| `Normal` | Neutral stat changes |
| `Late` | DoT (damage-over-time), poison |

`EffectSystem.AdvanceTick` sorts both `DueApplications` and `Expired` by `Phase` ascending. This guarantees a heal-before-damage ordering when both affect the same entity in the same tick.

---

## Stat integration seam

`IStatSystem.Get(entityId, ScoreId)` sums `IEffectSystem.GetModifiers(entityId, scoreId)` on top of base + equipment contributions. This is transparent to all existing consumers (combat, `score` command, etc.) — no call site changes when effects are added or removed. `GetModifiers` itself sums the stored effects **plus** every registered `IEffectContributor` (see [The contributor seam](#the-contributor-seam)), so a new modifier source (a passive ability, later equipment/auras) surfaces here with no consumer change.

`StatSystem` (domain) → `IEffectSystem` (core) is a legal downward dependency.

---

## The contributor seam

> Computed-score aggregation from heterogeneous, domain-owned sources. The rule is [INV-24](checklist.md); this is the worked instance and the third [layer-composition shape](01-layers.md#the-three-composition-shapes).

`EffectSystem` is the **aggregator** for everything that modifies a score: `GetModifiers(entityId, scoreId)` returns the base-independent modifiers `StatSystem.Get` folds on top of base + equipment. Effects stored in `EffectsComponent` are one source — but modifiers also come from **source-bound** origins that are *derived on read, never stored* (passive abilities; later equipment, auras, areas), owned by domain modules `EffectSystem` (core) may not reference (INV-2).

The seam is **dependency inversion**: a core-owned port `IEffectContributor`, DI-collected by `EffectSystem` as `IEnumerable<IEffectContributor>`. `GetModifiers`/`GetActive` sum the stored effects **plus** every contributor's output. Each source ships an adapter in its own module and registers it; `EffectSystem` never changes as sources are added (open/closed).

| Contributor | Source | Lands |
|---|---|---|
| (stored effects) | `EffectsComponent` | built (9-e) |
| `AbilityEffectContributor` | known `WhileKnown` passive abilities | built (11-a) |
| equipment / aura / area contributors | worn items, present auras/areas | future slices, same port |

**Why pull, not push.** A rejected alternative materializes derived modifiers into a stored component the aggregator reads. That caches derived state and reintroduces the "did I recompute when the source changed?" bug family compute-on-read exists to kill. Contributors **pull** at read time, so there is one source of truth (the worn item / known ability) and nothing to invalidate.

**Cross-source resolution — deferred.** Today contributors return *additive* modifiers, summed across sources. The moment a score needs cross-source resolution — caps, diminishing returns, or `HighestWins` spanning stored **and** contributed instances (resist caps, control-effect DR; feature-horizon §5) — the port must pass *contributions with metadata* (`Power`, `StackPolicy`, `Category`) so the aggregator resolves across sources instead of summing. A bounded, additive change to the port when a consumer needs it; not built now.

---

## Admin tooling

| Command | Access | Behaviour |
|---|---|---|
| `affect <target> <effectId> [power]` | Admin | Applies a registry effect; `[power]` is a testing-only override |
| `affects` | Player/Admin | Lists active effects on the caller (category, Power, remaining) |

`EffectRegistry` holds the hardcoded starter definitions (`empower`, `weaken`, `regen`, `poison`, `minor_curse`). Promotion to a data-file effect catalog is deferred (Category-3 balance data; tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)).

---

## Deferred / future

- **Aspect resolution (S3).** `EffectParams.Aspect` is carried on every effect but resolved to raw magnitude for now. Aspect math lands in S3.
- **Source-bound derivation.** Formalized as [the contributor seam](#the-contributor-seam) (INV-24). `AbilityEffectContributor` was built in slice 11-a and derives passive-ability `WhileKnown` modifiers at read time. Equipment/aura/area-derived effects fold in through the same `IEffectContributor` port in later slices.
- **Composite effects.** `CompositeEffectDefinition` (one name → several effects sharing a `Group`) deferred to the content slice that needs authored curses/blessings.
- **`Speed` targeting.** No `Speed` `ScoreId` yet; `haste` → `Speed` buff lands when a combat/initiative slice makes `Speed` a consumed derived score.

---

## Cross-references

- [`Core/Modules/Effects/`](../../Core/Modules/Effects/) — module source
- [`Core/ECS/Components/EffectsComponent.cs`](../../Core/ECS/Components/EffectsComponent.cs)
- [`docs/implementation-plans/effect-substrate.md`](../implementation-plans/effect-substrate.md) — behavioral spec (slice 9-e)
- [`docs/architecture/flows/flow-21-effect-tick.md`](flows/flow-21-effect-tick.md) — effect tick runtime flow
- [`docs/design/gameplay-model.md`](../design/gameplay-model.md) — Spine C, R5, R6
- [`docs/reference/systems.md`](../reference/systems.md) — `EffectSystem` catalog entry
- [`docs/reference/components.md`](../reference/components.md) — `EffectsComponent` catalog entry
