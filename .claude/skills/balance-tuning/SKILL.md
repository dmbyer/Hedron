---
name: balance-tuning
description: Use when changing any balance number or balance-affecting mechanic — tuning oracle weights/bands, progression curves, combat/regen/economy constants, the balance-standards document, or when a slice adds a new power source, a new tunable knob, or content that must land in a (Tier, Band) cell. Covers where every knob lives, how a contribution enters the power model, validating a change with a simulation sweep, and re-pinning CI goldens. Invoke for any deliberate balance change or when adding mechanics/content the balance system must account for.
---

# Balance Tuning

Balance in Hedron is one loop: **author → observe (power oracle) → validate (sim) → correct
(conformance) → re-pin (CI)**. This skill is how you change a balance number — or extend the
model when a new mechanic must feed it — without the model drifting.

> **Status.** Ships with slice `prog-5` of the Progression & Balance program (as-built history:
> [`docs/roadmap/completed/balance-doc-layer.md`](../../../docs/roadmap/completed/balance-doc-layer.md)).
> A separate `run-simulation` skill was deliberately **not** built: running sweeps is a
> designer/admin surface (Blazor Simulation page, `simulate` CLI), not an agent dev-loop step —
> the validation recipe below covers the cases where a dev-loop change *does* need a sweep.

**Authoritative:** [`docs/design/balance.md`](../../../docs/design/balance.md) (the knob catalog —
where every number lives) · [`docs/design/power-model.md`](../../../docs/design/power-model.md)
(snapshot-only extensibility rule) · [`power-budget-system.md`](../../../docs/features/progression/power-budget-system.md)
· [`simulation.md`](../../../docs/features/simulation/simulation.md) ·
[checklist](../../../docs/architecture/checklist.md) (INV-2 core-tier oracle, INV-18 content
tooling, INV-25/26 tests + determinism) · [config categories](../../../docs/architecture/05-configuration.md).

## First: find the knob's home

Look it up in the [knob catalog](../../../docs/design/balance.md#knob-catalog). The home decides
the change mechanics:

| Home | Change mechanics |
|---|---|
| **Balance-standards document** (`data/balance/standards.yaml` — oracle weights/bands, reference builds, tolerances) | Edit via the Blazor Standards page (`/standards`) or the YAML directly; structural validation refuses bad shapes; **restart-to-apply**. Keep the `AscensionConstants`/`CharacterDefaultsOptions` mirrors in sync or the load-time drift warning fires |
| **Category-3 constants** (`ProgressionConstants`, `AscensionConstants`, combat/regen inline math) | Edit the constant in the same commit as any dependent change; these are compiled — promotion to data happens only at a real OD-2 need |
| **`ProgressionConstants.GlobalXpScalar`** (prog-6) | The **macro** progression knob — multiplies every award from every source inside `ProgressionSystem`, so `2.0` exactly doubles progression speed. Reach for this to move overall pacing, not to fix one source |
| **`ProgressionConstants.Rules`** — the advancement table (prog-6) | One `AdvancementRule` per `XpSource`: `BaseAwardMin`/`Max`, `BaseChance`, `ChanceDecayPerImprovement`, `SourceScale`, `StaticTracks`, `AdvancementEligibility`. Read through `IAdvancementRuleRegistry`. Compiled precisely *because* these are golden-pinned; promotion to YAML needs the pinning contract reworked first. **`BaseChance`/`ChanceDecayPerImprovement` is a second rate-slowing curve** composing with the growing XP threshold — move one at a time |
| **Per-ability `XpScale` / `XpAttributeTrack`** (`AbilityRegistry`, prog-6) | Compiled rows; inspect with `defs ability <id>`. `XpAttributeTrack` is opt-in — an ability naming none grants rank only and no attribute power. A YAML/editor pipeline is [backlogged](../../../docs/roadmap/backlog.md) |
| **Per-mob `XpScale`** (prog-6) | `setmob <blueprintId> xpscale <value>` / YAML `xpScale:` / the Blazor `MobEditor` field. Non-negative; `0` makes that mob's kills award nothing |
| **Settings** (`CharacterDefaults:`, `Shop:`, `Death:`, `Heartbeat:`) | `appsettings.json`; already recompile-free |
| **Authored content** (item/mob stats, tier/band tags, ability definitions, loot ranges) | `setitem`/`setmob`/Blazor editors/YAML — normal content authoring (INV-18 tooling already exists) |

## Recipe: tune a number

1. Change it in its home (table above). Never copy a balance number into a second home (INV-27).
2. **Validate at scale if outcomes shift** (see validation recipe): combat-affecting → a combat
   scenario at the affected cell(s); progression-affecting → a `progressionRate` scenario.

   > ⚠️ **The `progressionRate` scenario currently models kill events only.** As of `prog-6` the
   > advancement table has three rows, but `ProgressionScenarioExecutor` still exercises
   > `AwardCombatExperience` exclusively — so a change to the **`AbilityUse` or `DamageTaken`**
   > rows, or to a per-ability `XpScale`, **will not move a golden and cannot be swept**. That is a
   > known blind spot, not a clean bill of health: those rows feed *attribute* tracks, which do
   > grant power. Until the sweep is generalized onto the `XpSource` vocabulary
   > ([backlog](../../../docs/roadmap/backlog.md) · [balance.md Known gaps](../../../docs/design/balance.md)),
   > tune those rows conservatively and say in the plan that the change is unvalidated.
3. **Re-pin CI goldens in the same commit.** `SimulationInvariantTests` (`Hedron.Tests/Simulation/`)
   pins win rates and kills-to-improvement at fixed seeds against current values — a deliberate
   tuning change updates those pins; a test going red you didn't expect means the change did more
   than intended.
4. If the change alters what the balance catalog says (a new knob, a moved home, a changed
   default worth noting), update [`balance.md`](../../../docs/design/balance.md) in the same PR.

## Recipe: a new mechanic that affects power

When a slice adds abilities, attributes, pools, gear mechanics, buffs — anything that changes how
strong an entity effectively is:

1. **Never teach the oracle the domain concept.** Apply the
   [power-model rule](../../../docs/design/power-model.md): (a) a stat-like quantity becomes a
   new `ScoreId` with a weight in the standards document, or (b) the owning module computes its
   own estimated contribution and callers sum it into the snapshot.
2. Route the live-gameplay side through the existing contribute-on-read seam
   (`IEffectContributor`, INV-24) — precedents: equipment, progression, ascension.
3. Give the new quantity a weight (even 0) in the standards document so designers can tune its
   balance contribution without recompiling.
4. If sim combatants should exercise it, extend the reference builds (standards document) and, if
   needed, the `ISimCombatantFactory`/policy seams — never fork a second combat model.

## Recipe: land content in a cell

1. Author the item/mob with a `Tier` + `Band`; watch the live power/cell readout in the Blazor
   editor (or `power`/`powerband` in-game).
2. Off-target? Use `IPowerBudgetSystem.TargetRange(tier, band)` as the design target, or let the
   Integrity page's conformance **preview/apply** scale the stat vector to the cell.
3. After bulk content changes, run the band-drift audit (Integrity page) — it doubles as a
   "content per cell" census.

## Recipe: validate with a simulation sweep

- **Headless:** `dotnet run --project Server -- simulate --scenario <path> [--seed <n>]` — runs a
  `ScenarioDefinition` YAML, writes the JSON report, prints a summary.
- **Interactive:** the Blazor Simulation page composes/saves/launches scenarios and browses the
  same reports; `MobEditor`/`ItemEditor` "Simulate vs reference" and the Standards page's
  "Re-run baseline sweep" prefill common cases.
- Combat scenarios grade against the standards' outcome tolerances; progression-rate scenarios
  report kills-to-improvement (descriptive until a tolerance is authored — see backlog).
- Determinism: identical scenario + seed ⇒ identical report; sweep comparisons are
  apples-to-apples only at the same seed and iteration count.

## Guardrails

- **The oracle stays snapshot-only** (INV-2 / power-model rule). If a tuning task tempts you to
  inject a service or domain type into `PowerBudgetSystem`, the design is wrong — route the
  contribution through the snapshot.
- **One number, one home** (INV-27). The two deliberate mirrors (standards ↔ `AscensionConstants`
  / `CharacterDefaultsOptions`) are guarded by load-time drift warnings — change both sides.
- **Curve shape discipline:** progression slows via the *threshold* curve, never by curving the
  power step — see [`edit-progression-system`](../edit-progression-system/SKILL.md) for anything
  touching XP sources/tracks/curves (that skill owns progression mechanics; this one owns the
  numbers-and-validation loop).
- **Audit and conformance are advisory** — never wire band-drift into a build/CI gate; variance
  within tolerance is expected content texture.
- **Known calibration gap:** the Ascension tier baseline currently has zero measurable combat
  effect (combat reads raw attack/defense/pools). Any slice touching `StatSystem`'s combat reads,
  `AscensionConstants`, or the win-rate tolerances must consult that backlog entry first — the
  pinned tests encode today's uncalibrated behavior, not the design target.
- **Chance/time in systems resolve through `IRandom`/`IClock`** (INV-26) — sim determinism
  depends on it.

## Related

- [`balance.md`](../../../docs/design/balance.md) — the knob catalog + maintenance contract.
- [`power-model.md`](../../../docs/design/power-model.md) — how power inputs extend.
- [`edit-progression-system`](../edit-progression-system/SKILL.md) — progression mechanics
  (sources, tracks, rule-table promotion).
- [`power-budget-system.md`](../../../docs/features/progression/power-budget-system.md) ·
  [`simulation.md`](../../../docs/features/simulation/simulation.md) — the systems this skill drives.
- [`add-tests`](../add-tests/SKILL.md) — for the re-pinned invariants and any new seams' coverage.
