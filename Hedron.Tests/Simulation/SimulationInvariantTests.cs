using System.Threading;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 3 — promoted CI invariant subset (Postcondition 10). Live engine runs at a fixed seed
    /// and small N against the compiled <c>BalanceStandardsDefaults</c> tolerances — deliberately
    /// thin; heavy sweeps stay out of CI (the seed brief). A regression pin, not a hypothesis test:
    /// the seed and N are chosen once so the fixed outcome is reproducible, not statistically sampled.
    /// </summary>
    public sealed class SimulationInvariantTests
    {
        [Fact]
        public void EqualCell_ReferenceBuild_WinRateWithinTolerance()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "ci-equal-cell", seed: 2026, iterations: 200, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var report = runner.Run(scenario);

            // Golden pin at (seed 2026, N 200): 110/90, no draws. Re-pinned by combat-effective-scores
            // (was 106/94): both tier-2 sides now gain the tier Body in AttackPower/Defense and spawn
            // full at the effective HpMax — symmetric, so the split stays within tolerance.
            Assert.Equal(110, report.SideAWins);
            Assert.Equal(90, report.SideBWins);
            Assert.Equal(0, report.Draws);

            var verdict = Assert.Single(report.Verdicts);
            Assert.Equal("equalCellWinRate", verdict.Name);
            Assert.True(verdict.Passed, verdict.Reason);
        }

        /// <summary>
        /// The tier baseline reaches combat (combat-effective-scores): <c>AscensionEffectContributor</c>
        /// folds it onto <c>ScoreId.Body</c>/<c>HpMax</c>, and since that slice <c>StatSystem</c> derives
        /// AttackPower/Defense from effective Body and combatants spawn full at the effective HpMax. Before
        /// it the tier had zero combat effect (pinned 106/94, failing the floor); the one-tier-higher build
        /// now wins almost every fight. The <i>magnitude</i> of the tier step is Phase 6 tier tuning
        /// (<c>AscensionConstants</c>, the standards' floor) — this test pins today's measured outcome and
        /// verdict, it does not assert a design target. Tracked in <c>docs/roadmap/backlog.md</c>.
        /// </summary>
        [Fact]
        public void OneBandHigher_ReferenceBuild_WinRate_Pinned()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            // (3,1) and (2,3) are the one global-band-index-apart pair that also crosses a tier
            // boundary — the only +1 gap with any real power difference (band alone grants none).
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "ci-one-band-higher", seed: 2026, iterations: 200, maxTicksPerRun: 100, tierA: 3, bandA: 1, tierB: 2, bandB: 3);

            var report = runner.Run(scenario);

            // Golden pin at (seed 2026, N 200): 199/1 (was 106/94 before combat-effective-scores).
            Assert.Equal(199, report.SideAWins);
            Assert.Equal(1, report.SideBWins);
            Assert.Equal(0, report.Draws);

            var verdict = Assert.Single(report.Verdicts);
            Assert.Equal("higherBandWinRateFloor", verdict.Name);
            Assert.True(verdict.Passed, verdict.Reason);
        }

        /// <summary>
        /// sim-3 Test 9 — determinism cross-surface pin: the extended <c>Run</c> signature (CT +
        /// progress callback, as the editor calls it) reproduces the same golden expectations as
        /// the bare CLI call above at the identical (scenario, seed) — CLI and editor byte-identity.
        /// </summary>
        [Fact]
        public void EqualCell_ReferenceBuild_ExtendedRunSignature_MatchesCliGoldenExpectations()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "ci-equal-cell", seed: 2026, iterations: 200, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);
            var completed = 0;

            var report = runner.Run(scenario, cancellationToken: CancellationToken.None, onRunCompleted: () => Interlocked.Increment(ref completed));

            Assert.Equal(110, report.SideAWins);
            Assert.Equal(90, report.SideBWins);
            Assert.Equal(0, report.Draws);
            Assert.Equal(200, completed);
        }

        // ── ProgressionRate (sim-4, Postcondition 12) ────────────────────────────

        [Fact]
        public void ProgressionRate_EqualPowerReferenceBuild_KillsToFirstImprovement_WithinConstantsDerivedBounds()
        {
            // Equal-power reference builds (tier 2 band 2 vs tier 2 band 2) give an anti-grind
            // scale of exactly 1.0 -> the Body award every kill is the raw roll, uniformly in
            // [CombatAwardMin, CombatAwardMax] = [8, 12] (ProgressionConstants). Reaching
            // ThresholdBase (100) therefore takes between ceil(100/12)=9 and ceil(100/8)=13 kills,
            // regardless of the actual random sequence -- and the cap (50) is far above the 13-kill
            // worst case, so every run is guaranteed to reach the target. Since the relative-power
            // slice the ratio is regathered per kill on EFFECTIVE power, but it stays exactly 1.0
            // until the subject's first improvement, so this pin did not move with that change.
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "ci-progression-first-improvement", seed: 2026, iterations: 200, maxKillsPerRun: 50,
                targetTrack: ScoreId.Body, targetImprovements: 1,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var report = runner.Run(scenario);
            var progression = report.ProgressionRate!;

            // Golden pin at (seed 2026, N 200): min 10, max 12, mean 10.48 — within the
            // constants-derived [9, 13] bound above, as expected.
            Assert.Equal(200, progression.RunsReachedTarget);
            Assert.Equal(10, progression.KillsToTarget.Min);
            Assert.Equal(12, progression.KillsToTarget.Max);
            Assert.Equal(10.48, progression.KillsToTarget.Mean);

            var verdict = Assert.Single(report.Verdicts, v => v.Name == "targetReached");
            Assert.True(verdict.Passed, verdict.Reason);
        }

        [Fact]
        public void ProgressionRate_MilestoneGapMonotonicity_KillsBetweenSuccessiveImprovementsNeverDecrease()
        {
            // Equal power + a generous cap (worst case ceil(200/8)=25 kills for 3 improvements at
            // ratio 1.0; after each improvement the subject outgrows the victim slightly on
            // effective power and awards scale a little below the roll, still far inside cap 50)
            // guarantees every run reaches the full target, so every milestone's mean is
            // averaged over the identical full run population. Each individual run's own kill
            // counter is non-decreasing by construction (milestones are appended in encounter
            // order), so the averaged sequence must be non-decreasing too -- a mathematical
            // certainty here, not a statistical tendency, independent of the RNG sequence.
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "ci-progression-monotonicity", seed: 2026, iterations: 100, maxKillsPerRun: 50,
                targetTrack: ScoreId.Body, targetImprovements: 3,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var report = runner.Run(scenario);
            var progression = report.ProgressionRate!;

            Assert.Equal(100, progression.RunsReachedTarget);
            Assert.Equal(3, progression.MeanMilestoneKills.Count);
            for (var m = 0; m < progression.MeanMilestoneKills.Count - 1; m++)
            {
                Assert.True(
                    progression.MeanMilestoneKills[m] <= progression.MeanMilestoneKills[m + 1],
                    $"milestone {m} mean kills ({progression.MeanMilestoneKills[m]}) exceeded milestone {m + 1} ({progression.MeanMilestoneKills[m + 1]})");
            }
        }

        [Fact]
        public void ProgressionRate_ExtendedRunSignature_MatchesBareCallDeterministically()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "ci-progression-determinism", seed: 2026, iterations: 50, maxKillsPerRun: 50,
                targetTrack: ScoreId.Body, targetImprovements: 1,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);
            var completed = 0;

            var bare = runner.Run(scenario);
            var extended = runner.Run(
                scenario, cancellationToken: CancellationToken.None, onRunCompleted: () => Interlocked.Increment(ref completed));

            Assert.Equal(50, completed);
            Assert.Equal(bare.ProgressionRate!.RunsReachedTarget, extended.ProgressionRate!.RunsReachedTarget);
            Assert.Equal(bare.ProgressionRate.KillsToTarget, extended.ProgressionRate.KillsToTarget);
        }
    }
}
