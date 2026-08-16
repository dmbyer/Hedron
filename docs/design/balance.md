# Balance — Catalog & Maintenance Guide

> The balance reference for Hedron: what "balanced" means mechanically, **where every tunable
> number lives**, which surfaces observe balance, and the workflow that keeps the balance system
> current as content and mechanics expand. Companion to [`power-model.md`](power-model.md) (the
> oracle's extensibility rule) — that doc says how power *inputs* may be added; this doc says
> where the *numbers* live and how tuning happens. Shipped as slice `prog-5` of the
> [Progression & Balance program](../roadmap/completed/balance-doc-layer.md).
>
> Day-to-day tuning work goes through the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md)
> skill, which operationalizes this catalog into recipes. This doc is the reference; the skill is
> the how-to.

## The model in one screen

Balance in Hedron is anchored to one shared vocabulary and one oracle:

- **Power** — a heuristic scalar computed by [`IPowerBudgetSystem.Estimate`](../features/progression/power-budget-system.md)
  (`Core/Systems/PowerBudgetSystem.cs`): a weighted sum over a caller-supplied `ScoreId → magnitude`
  snapshot plus an additive per-tier baseline. Snapshot-only by rule — see [`power-model.md`](power-model.md).
- **Tier × Band** — `Classify(power)` maps a power scalar onto **Tier 0–6** (character-wide,
  Ascension-gated, mechanical) × **Band 1–3** (low/mid/high within a tier, descriptive). The
  inverse `TargetRange(tier, band)` gives the authored power range for forward design.
- **Standards** — the designer-authored expected values: reference builds per (Tier, Band),
  outcome tolerances, drift tolerance, and the oracle's tunables, all in the YAML
  balance-standards document (see catalog below).
- **Validation** — the [simulation engine](../features/simulation/simulation.md) runs
  deterministic batch scenarios (combat outcomes, progression rates) and grades results against
  the standards; a thin subset of derived invariants is CI-pinned in `Hedron.Tests/Simulation/`.
- **Correction** — the band-drift audit (`IBalanceAuditSystem`) flags content whose projected
  power has drifted from its authored (Tier, Band); the conformance fitter
  (`ITemplateConformanceSystem`) scales it back. Both soft/advisory, never a build gate.

The loop: **author → observe (oracle) → validate (sim) → correct (conformance) → re-pin (CI)**.

## Knob catalog

Every tunable balance number, by home. "Category" is the
[configuration strategy](../architecture/05-configuration.md) bucket: **2** = authored content
data (designer-editable), **3** = system-math constants (compiled, co-located with the owning
system), **settings** = `appsettings.json` options.

### Balance-standards document — Category 2 (the primary tuning surface)

`data/balance/standards.yaml` (path via `Balance:StandardsPath`), loaded/validated/saved by
`IBalanceStandardsStore`, served by `IBalanceStandardsRegistry`, authored on the Blazor
**Standards page** (`/standards`). **Restart-to-apply** (composed into the oracle's constructor
once at boot — see [live-reload deferral](../roadmap/backlog.md)). Compiled fallback:
`PowerBudgetTunables.Default` + `BalanceStandardsDefaults`.

| Knob | What it tunes |
|---|---|
| `Weights` (per `ScoreId`) | How much each score contributes to estimated power — the oracle's entire formula |
| `BandSpan` | Width of one band's power range (must stay `< TierSpan / BandsPerTier`) |
| `BandsPerTier` | Sub-bands per tier (3: low/mid/high) |
| `ReferenceBaseScores` | The reference base build bands are derived from (mirrors `CharacterDefaultsOptions` — load-time drift warning) |
| `MaxTier` / `TierBaselineStep` / `TrackedScores` | Mirror of `AscensionConstants` for the oracle's tier math (load-time drift warning) |
| Reference builds per (Tier, Band) | Gear-equivalent stat bonuses + ability-kit field per cell — the sim's canonical combatants |
| Outcome tolerances | Expected sim outcomes (equal-cell win-rate window, higher-band win-rate floor) |
| `BandDriftTolerance` | How many bands off authored (Tier, Band) content may project before the audit flags it |

### System-math constants — Category 3 (compiled, co-located)

| Home | Knobs | Notes |
|---|---|---|
| `Core/Modules/Progression/ProgressionConstants.cs` | `PowerPerImprovement`, `ThresholdBase`, `ThresholdIncrement`, **`GlobalXpScalar`**, `AntiGrindFloorRatio`, `AntiGrindCap`, `CombatTracks` | The progression curve — see [`progression-system.md`](../features/progression/progression-system.md). `GlobalXpScalar` is the **macro knob**: it multiplies every award from every source, so `2.0` exactly doubles progression speed. Golden numbers CI-pinned in `SimulationInvariantTests`; a tuning change **re-pins in the same commit** |
| `Core/Modules/Progression/ProgressionConstants.cs` → `Rules` | Per `XpSource` row: `BaseAwardMin`/`Max`, `BaseChance`, `ChanceDecayPerImprovement`, `SourceScale`, `StaticTracks`, `AdvancementEligibility` | The **advancement table** (prog-6) — one row per wired XP source, read through `IAdvancementRuleRegistry`. `BaseChance` and `ChanceDecayPerImprovement` are the second rate-slowing curve, composing with the growing XP threshold. Compiled rather than YAML precisely *because* these are golden-pinned; promotion needs the pinning contract reworked first |
| `Core/Modules/Abilities/AbilityRegistry.cs` | Per-ability `XpScale`, `XpAttributeTrack` | Granular progression knobs on the compiled ability rows (prog-6). `XpAttributeTrack` is opt-in: an ability that names none grants **rank only** and adds no attribute power. Inspect via `defs ability <id>`; a YAML/editor pipeline is [backlogged](../roadmap/backlog.md) |
| `Core/Modules/Ascension/AscensionConstants.cs` | `MaxTier`, `TierBaselineStep`, `TrackedScores`, `UnlocksForTier` | The *real gameplay* tier power (the standards document mirrors it for the oracle; keep in sync — drift warns at load). Known calibration gap: the baseline currently has no measurable combat effect (see Known gaps) |
| `Core/Modules/Combat/Systems/CombatSystem.cs` | Hit threshold (`10 + Defense`), damage roll shape, minimum damage | Inline in the round-resolution math; promotion candidate if combat depth lands |
| `Core/Modules/Regeneration/Systems/RegenerationSystem.cs` | `RegenAmount`, `IdleIntervalTicks`, resting multiplier | Deliberately isolated for cheap later promotion ([backlog](../roadmap/backlog.md)) |
| Effect `PowerScaling` specs | Per-effect potency / stack-rank `Power` at apply time | A *different* "Power" from the oracle's — see the distinction in [`power-model.md`](power-model.md#distinct-from-effect-power) |

### Settings — `appsettings.json` options

| Section / class | Balance-relevant knobs |
|---|---|
| `CharacterDefaults:` (`CharacterDefaultsOptions`) | Starting attributes, pools, starting abilities — the Tier-0 baseline the reference build mirrors |
| `Shop:` (`ShopOptions`) | `BuyRatio`, `SellRatio`, `RestockInterval`, `BuyBackRetention`, `DefaultTillSeed` — the economy spread |
| `Death:` (`DeathOptions`) | HP floor, bleed-out pacing |
| `Heartbeat:IntervalMs` | Real-time length of one tick — the time base under regen, cooldowns, combat rounds, restock |

### Authored content — Category 2 (per-definition, YAML + editors)

Per-item `statBonuses`/`value`/`tier`/`band`, per-mob attributes/`tier`/`band`/**`xpScale`** (the
per-mob kill-experience multiplier, prog-6 — `0` makes a mob award nothing), mob
`CurrencyLoot` ranges, ability definitions (costs, cooldowns, effect magnitudes), shop stock and
till seeds. Authored via `setitem`/`setmob`/YAML/the Blazor editors; each definition's projected
power and cell fit shows in the editor readout as it is edited.

## Observability surfaces

| Surface | What it shows |
|---|---|
| `power` / `powerband` (admin commands) | An entity's/definition's estimated power + Tier×Band; the band anchor table |
| Blazor `ItemEditor` / `MobEditor` readout | Live projected power + cell fit (band-mismatch flag) while authoring |
| Blazor **Integrity** page — band-drift audit | Every item/mob past `BandDriftTolerance`, plus content counts per (Tier, Band); per-row and bulk conformance preview/apply |
| Blazor **Standards** page | The standards document + live derived target-range preview |
| Blazor **Simulation** page / `simulate` CLI run-mode | Batch scenario runs (combat outcome, progression rate) graded against the standards; JSON report artifacts |
| `progress` (player command) | Per-track XP/improvements — the progression curve as experienced |
| `Hedron.Tests/Simulation/` CI invariants | Pinned golden outcomes (win rates, kills-to-improvement) that catch balance regressions on every PR |

## Keeping balance current (the maintenance contract)

Balance is a **living featureset**: every slice that adds a mechanic or content either feeds the
model or tunes against it. The rules, each enforced at the normal per-slice gates:

1. **A new power source never changes the oracle.** A new ability family, attribute, pool, or
   gear mechanic folds in per [`power-model.md`](power-model.md): a new `ScoreId` weight, or a
   caller-summed estimated contribution. The advisor/planner ask this question at intake.
2. **New content is authored against a cell.** Give items/mobs a (Tier, Band), check the editor
   readout, and let the audit/conformance loop catch drift. `TargetRange(tier, band)` is the
   forward-design query (and the seam future procedural generation will consume).
3. **A tuning change re-validates and re-pins.** Constants and standards edits run the relevant
   sim scenario (combat sweep or progression-rate sweep) and re-pin any affected CI goldens in
   the same commit. Recipes: [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md).
4. **New expected outcomes become standards, not constants.** When a designer can state an
   expectation ("a one-band-higher build should win ≥65%"), it belongs in the standards document
   (tolerances), where the sim grades it — not hardcoded in a test.
5. **This catalog stays current.** A slice that adds a tunable knob, a new observability
   surface, or a new standards family adds its row here in the same PR (the balance instance of
   the INV-20 discipline).

## Known gaps (tracked in [`backlog.md`](../roadmap/backlog.md))

- **Ascension tier baseline has no measurable combat effect** — the baseline folds into
  `Body`/`HpMax` via `IStatSystem.Get`, but combat reads raw attack/defense/pool values; pinned
  at discovery (sim-2), awaiting a deliberate balance-tuning slice.
- **The `progressionRate` sim is blind to use-based accrual** *(prog-6, stated at ship)* — the
  ability-use and damage-taken rules feed **attribute** tracks, which do grant power through
  `ProgressionEffectContributor`, but `ProgressionScenarioExecutor` exercises
  `AwardCombatExperience` exclusively. No golden moved when prog-6 shipped, and that is because
  the sim cannot see the new sources — **not** because they are power-neutral. Bounded by
  deliberately conservative defaults (low `BaseChance`, meaningful `ChanceDecayPerImprovement`) so
  the unvalidated rate is a slow drift rather than a step change. The rule table now gives the
  generalization a vocabulary to point at; see the backlog entry.
- **Progression-rate expectation tolerances unpromoted** — the sim's progression-rate verdict is
  descriptive-only until a designer states a kills-to-improvement expectation.
- **Live standards reload** — Standards-page edits are restart-to-apply.
- **Mob projection-vs-spawn attribute defaulting divergence** — an authored-zero attribute
  projects weaker than it spawns.
- **Balance-reviewer agent** — a stretch backstop that would sweep balance-affecting diffs with
  sim runs; build when balance regressions from expansion become a recurring cost.

## Related

- [`power-model.md`](power-model.md) — the snapshot-only extensibility rule for power inputs.
- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) —
  the oracle, standards registry, audit, and conformance fitter designs.
- [`../features/progression/progression.md`](../features/progression/progression.md) ·
  [`../features/simulation/simulation.md`](../features/simulation/simulation.md) — the features
  this catalog spans.
- [`../architecture/05-configuration.md`](../architecture/05-configuration.md) — the category
  model and the OD-2 promotion trigger (constants → data when recompile-free iteration is real).
- [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) ·
  [`edit-progression-system`](../../.claude/skills/edit-progression-system/SKILL.md) — the
  operational skills.
