# Progression substrate — slice prog-1 (completed)

> Implemented on branch `claude/reverent-morse-64b872`, 2026-07-05. Living docs: [`features/progression/progression.md`](../../features/progression/progression.md) · [`features/progression/progression-system.md`](../../features/progression/progression-system.md).

## Outcome

Experience-driven progression (gameplay-model Spine E) landed as a new `Progression` module. Every kill scales a killer's `Body` and `HpMax` tracks by an anti-grind factor; crossing a track's growing cumulative threshold grants a permanent linear power step, pulled on read into `IStatSystem.Get` through a new `IEffectContributor` registrant — never stored, never cached. Players inspect their tracks with `progress`. This is slice 1 of the five-slice Progression & Balance program; slices 2–5 (Tier/Ascension, power-budget oracle, sim harness, agentic layer) remain unbuilt.

## Behavior digest

- **Award:** `AwardExperience(entity, track, amount, source)` adds the (already anti-grind-scaled) amount to cumulative XP, creating the component/entry on first award; a non-positive amount is a no-op.
- **Improve:** `TryImprove` increments a track's improvement count once per cumulative threshold crossed (`ThresholdBase + k × ThresholdIncrement`); a single large award can cross several thresholds in one call.
- **Contribution:** `IStatSystem.Get(entity, score)` folds `PowerPerImprovement × improvementCount(score)` via `ProgressionEffectContributor`, pulled on read — no `EffectsComponent` entry, no cached field.
- **Anti-grind:** `AwardCombatExperience` scales the randomized per-track base amount by `clamp(victimPower/killerPower, floor→0, cap)` computed from **raw** `AttributesComponent` fields (not `IStatSystem` — see Decisions).
- **Events:** one `ExperienceAwardedEvent` per positive-amount track; one `TrackImprovedEvent` per threshold crossed.
- **Persistence:** `ProgressionComponent` is `[Persistent]`, attached only to persistent (player) entities — never world content.
- **Main flow:** `MobDiedEvent` (published pre-destroy by `CombatMobDeathHandler`) → `ExperienceAwardHandler` (one of three independent subscribers, alongside `CurrencyLootHandler` and `SpawnSystem`) → `IProgressionSystem.AwardCombatExperience` → publishes the two events → later `IStatSystem.Get` reflects the step.

## Shipped pieces

| Surface | Location |
|---|---|
| `ProgressionComponent` (`[Persistent]`) | `Core/Modules/Progression/Components/ProgressionComponent.cs` |
| `IProgressionSystem` / `ProgressionSystem` | `Core/Modules/Progression/Systems/IProgressionSystem.cs` · `ProgressionSystem.cs` |
| `ProgressionConstants` | `Core/Modules/Progression/ProgressionConstants.cs` |
| `ProgressionEffectContributor` (`IEffectContributor`) | `Core/Modules/Progression/ProgressionEffectContributor.cs` |
| `XpSource` enum | `Core/Modules/Progression/XpSource.cs` |
| `ExperienceAwardedEvent` / `TrackImprovedEvent` | `Core/Modules/Progression/Events/` |
| `ExperienceAwardHandler` | `Core/Modules/Progression/Handlers/ExperienceAwardHandler.cs` |
| `ProgressCommand` / `ProgressDisplayMessage` | `Core/Modules/Progression/Commands/ProgressCommand.cs` · `Core/Output/ProgressDisplayMessage.cs` |
| `ProgressionModule.AddProgressionModule` | `Core/Modules/Progression/ProgressionModule.cs` — registered in `Server/CompositionRoot.cs`; `ExperienceAwardHandler` subscribed to `MobDiedEvent` in `Server/Program.cs` |
| `TelnetOutputFormatter.FormatProgress` | `Core/Output/TelnetOutputFormatter.cs` |

## Tests shipped

- **Tier 1** — `ProgressionSystemTests` (award/no-op, multi-threshold crossing, growing-threshold monotonicity, three anti-grind cases, determinism) and `ProgressionEffectContributorTests` (modifier fold, never-materialized) in `Hedron.Tests/Progression/`.
- **Tier 2** — `ExperienceAwardHandlerTests` (peer-kill fan-out, no-crossing/crossing event counts, killer-zero discard).
- **Tier 3** — `ProgressionAwardFlowTests` (real `CombatSystem` + `ExperienceAwardHandler` + dispatching bus + seeded `IRandom`; asserts XP/effective-score postconditions and the `progress` command's typed output).
- **Tier 4** — two additions to `Hedron.Tests/Persistence/RoundTripTests.cs` (`ProgressionComponent` round-trip; world-content entities never carry it).
- `dotnet test` green — 933 tests total (up from 913 pre-slice).

## Decisions

- **Reuses `IEffectContributor`, not a new `IProgressionContributor`.** `IStatSystem.Get` already folds exactly one aggregation path (`IEffectSystem.GetModifiers`'s DI-collected contributor list); `ProgressionEffectContributor` is a third registrant alongside equipment and abilities, needing zero interface change. Owner-approved 2026-07-04.
- **Anti-grind proxy reads raw `AttributesComponent`, not `IStatSystem` (as-built fix, not in the original plan).** Wiring the DI graph surfaced a genuine circular dependency: `IStatSystem` → `IEffectSystem` → the contributor list → `ProgressionEffectContributor` → `IProgressionSystem` → `ProgressionSystem` → `IStatSystem`. `ProgressionSystem`'s anti-grind proxy now reads raw attribute fields directly via `EntityService`, breaking the cycle. This is an acceptable simplification for an explicitly temporary heuristic (slice 3's `IPowerBudgetSystem` replaces it wholesale) and generalizes: any future `IEffectContributor`'s backing system must read raw component data for its inputs, never a computed (`IStatSystem`/`IEffectSystem`) value — that's the *output* seam a contributor feeds, not an input it may consume.
- **Combat award has a randomized base, scaled by a deterministic anti-grind factor.** The base per-track amount is drawn via `IRandom` (`CombatAwardMin`..`CombatAwardMax`); the anti-grind ratio itself is a pure function of state. This gives the "any variance resolves through `IRandom`" postcondition concrete substance while keeping the anti-grind math itself trivially deterministic and testable.
- **Threshold curve is linear-cumulative** (`ThresholdBase + k × ThresholdIncrement`), satisfying the plan's literal test requirement (`threshold(k+1) > threshold(k)`) without over-building a superlinear curve the resolved decisions never asked for.
- **`ProgressDisplayMessage` lives in `Core/Output/`, not a per-module `Messages/` folder** — the plan's first sketch — matching the established convention (`ScoreDisplayMessage`, `EquipmentDisplayMessage`, `InventoryListMessage` all live there).
- **Module registered in `CompositionRoot.Register`, not `Program.cs`** — mirrors `EconomyModule`; the Blazor content-authoring host's `StatSystem` needs the contributor too, or it silently under-counts progression (a latent INV-24 gap).

## Deviations / Follow-ups

- **Deviation:** `ProgressionSystem`'s constructor is `(EntityService, IRandom)`, not `(EntityService, IStatSystem, IRandom)` as the plan's Systems/handlers table listed — see the anti-grind-proxy decision above. Documented in the plan before deletion; the spec's *intent* (contribute-on-read via the existing port, a killer-vs-victim anti-grind ratio) is unchanged.
- **Follow-up (backlog):** the anti-grind proxy is a one-method stand-in; slice 3's `IPowerBudgetSystem` replaces it wholesale.
- **Follow-up (deferred, not a gap):** an admin `setprogress`-style hand-set command — no balance/testing task needs it yet; tracked in [`../backlog.md`](../backlog.md).
- **No other deviations.** WP-1/2/3 shipped as scoped; the Test plan's five tiers are all present.
