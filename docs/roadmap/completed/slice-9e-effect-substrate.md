# Phase 3 Slice 9-e — Effect Substrate

**PR:** #100 · **Spec:** [`../../implementation-plans/effect-substrate.md`](../../implementation-plans/effect-substrate.md) · **Design:** [`../../architecture/effects.md`](../../architecture/effects.md) · **Gameplay-model spine:** S2

> Ledger backfilled retroactively (merged in #100 without a `done.md`/`completed/` entry at the time).

## Outcome

Introduced the **effect model** and the core system that applies, stacks, orders, ticks, and persists effects. An `Effect` is a parameterized instance of a small fixed set of **kinds**, carrying a target `ScoreId` (S1), a `Category`, a computed `Power`, a `Lifetime`, a `StackPolicy`, and a resolution `Phase`. Effects live in a single `EffectsComponent`; persistence is lifetime-filtered (only `UntilRemoved` entries are written). This is the bedrock most later spines depend on — skills/spells produce effects, potions apply them, curses/auras are effects.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `Effect` record + enums (`EffectKind`, `EffectCategory`, `EffectLifetime`, `StackPolicy`, `EffectPhase`) | `Core/Modules/Effects/` | `StatModifier`/`Instant`/`Periodic`/`GrantFlag` wired; `GrantAbility`/`Trigger`/`TransformModifier` enum-only (deferred) |
| `EffectsComponent` (`[Persistent]` + `EffectsComponentJsonConverter`) | `Core/ECS/Components/` | converter writes only `UntilRemoved` |
| `IEffectSystem`/`EffectSystem` (core) | `Core/Modules/Effects/Systems/` | `Apply`/`Remove`/`RemoveByCategory`/`GetActive`/`GetModifiers`/`AdvanceTick`; returns results, never publishes/persists |
| `PowerScaling` (formula registry: `fixed`, `byAttunement`) | `Core/Modules/Effects/` | Power computed from source **base** stats (acyclic) |
| `EffectRegistry` (`empower`/`weaken`/`regen`/`poison`/`minor_curse`) | `Core/Modules/Effects/` | hardcoded Category-3 starter set |
| `StatSystem.Get` folds `GetModifiers` | `Core/Modules/Stats/` | transparent to combat/`score` — no consumer change |
| `EffectTickHandler` (heartbeat → `AdvanceTick`, phase-ordered) | `Core/Modules/Effects/Handlers/` | applies periodic pool writes via `IAttributeSystem` |
| Admin `affect` + player `affects` | `Core/Modules/Effects/Commands/` | apply/inspect; `[power]` is a testing override |
| `EffectAppliedEvent`/`EffectExpiredEvent`/`EffectAppliedByAdminEvent` | `Core/Modules/Effects/Events/` | thin, past-tense |

## Notable design points

- **Single list, lifetime-filtered** — one `EffectsComponent` with one `List<Effect>`; `Lifetime` alone decides persistence (no Persistent/Transient split).
- **`EffectSystem` is core and self-contained** — reads the source's **base** stats to evaluate `PowerScaling` (never `IStatSystem`, avoiding an `Effect↔Stat` cycle); periodic magnitudes are written by the *handler*, not the system.
- **Power is required and computed at apply time** (INV-8) — callers never hand-pass it (except the admin `[power]` testing override).
- **Aspect carried, not resolved** (S3 deferred); **kinds wired vs deferred** is additive — adding a handler later needs no model change.
- **`WhileKnown`/source-bound derivation deferred to S4** — the seam (`GetModifiers`/`GetActive`) is the same; slice 11-a supplies the ability-derived fold via the `IEffectContributor` port (later canonized as INV-24).

## Deviations from the use-case doc

None — shipped per spec; the deferred kinds and aspect resolution were explicitly scoped out.

## Follow-ups unlocked

- **10 (death/respawn):** `IEffectSystem.RemoveImpermanent` (keys off `Lifetime`) drops timed effects on death, keeps `UntilRemoved`.
- **11-a (abilities):** abilities **produce** effects via `IEffectSystem.Apply`; passive `WhileKnown` effects fold in via the new contributor seam.
- **11-c (regen):** the heartbeat-tick precedent (`EffectTickHandler`) is mirrored by the regeneration tick.
