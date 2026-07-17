using System.Collections.Generic;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Web.Services
{
    /// <summary>
    /// Composes the "Simulate vs reference" entry-point prefill for <c>MobEditor</c>/<c>ItemEditor</c>
    /// (sim-3 Postcondition 8) — pure scenario-composition logic kept out of the razor pages so it is
    /// independently testable (parity with <see cref="BaselineSweep"/>). Reads the <b>last-saved</b>
    /// template via the catalog (the editors' own load path); never re-derives verdict math itself —
    /// every score comes from an existing computed seam (<see cref="IBalanceStandardsRegistry.ReferenceSnapshot"/>,
    /// <see cref="IMobPowerProjectionSystem"/>/<see cref="IItemPowerProjectionSystem"/>,
    /// <see cref="IPowerBudgetSystem"/>).
    /// </summary>
    public static class SimulationPrefill
    {
        public const string PolicyId = "cooldown-first";
        public const int Seed = 1234;
        public const int Iterations = 200;
        public const int MaxTicksPerRun = 100;

        /// <summary>
        /// The authored mob vs the <see cref="CombatantSourceKind.ReferenceBuild"/> of its authored
        /// (Tier, Band) cell — falling back to the computed cell (via <paramref name="powerBudget"/>/
        /// <paramref name="mobProjection"/>, the editor's own oracle readout) when the mob is unbanded.
        /// </summary>
        public static ScenarioDefinition ForMob(
            MobTemplate template,
            IPowerBudgetSystem powerBudget,
            IMobPowerProjectionSystem mobProjection)
        {
            var cell = ResolveCell(
                template.Tier, template.Band,
                () => powerBudget.Classify(powerBudget.Estimate(mobProjection.Project(template), template.Tier)));

            var sideA = new CombatantSpec(
                CombatantSourceKind.MobTemplate, PolicyId, MobBlueprintId: template.BlueprintId,
                Tier: cell.Tier, Band: cell.Band);
            var sideB = new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: cell.Tier, Band: cell.Band);

            return Compose($"vs-reference.{template.BlueprintId}", sideA, sideB);
        }

        /// <summary>
        /// "A baseline character of this item's cell, wearing this item" (an <see cref="CombatantSourceKind.Inline"/>
        /// composition — the per-score sum of the cell's <see cref="IBalanceStandardsRegistry.ReferenceSnapshot"/>
        /// and the item's projected <see cref="PowerSnapshot"/>) vs a bare
        /// <see cref="CombatantSourceKind.ReferenceBuild"/> of the same cell. No weight/band/verdict
        /// math is re-derived — both snapshots come from existing computed seams; this only sums them.
        /// </summary>
        public static ScenarioDefinition ForItem(
            ItemTemplate template,
            IBalanceStandardsRegistry standards,
            IPowerBudgetSystem powerBudget,
            IItemPowerProjectionSystem itemProjection)
        {
            var cell = ResolveCell(
                template.Tier, template.Band,
                () => powerBudget.Classify(powerBudget.Estimate(itemProjection.Project(template), template.Tier)));

            var baseline = standards.ReferenceSnapshot(cell.Tier, cell.Band);
            var itemSnapshot = itemProjection.Project(template);
            var summed = SumScores(baseline.Scores, itemSnapshot.Scores);

            var sideA = new CombatantSpec(
                CombatantSourceKind.Inline, PolicyId, Tier: cell.Tier, Band: cell.Band,
                Inline: new InlineStatBlock(summed, System.Array.Empty<string>()));
            var sideB = new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: cell.Tier, Band: cell.Band);

            return Compose($"vs-reference.{template.BlueprintId}", sideA, sideB);
        }

        /// <summary>
        /// Prefill for a progression-rate scenario's <c>ticksPerKill</c> field (sim-4): a chosen
        /// combat report's mean time-to-kill, or <see langword="null"/> when the report isn't a
        /// decisive combat report (wrong kind, or every run was a draw). Pure — the engine never
        /// reads report files as input; this is an editor-side, prefill-only convenience.
        /// </summary>
        public static double? TicksPerKillFrom(SimulationReport report)
        {
            if (report.Scenario.Kind != ScenarioKind.Combat)
                return null;

            var decisiveRuns = report.SideAWins + report.SideBWins;
            return decisiveRuns > 0 ? report.TicksToKill.Mean : null;
        }

        private static ScenarioDefinition Compose(string name, CombatantSpec sideA, CombatantSpec sideB) => new(
            ScenarioKind.Combat, name, Seed, Iterations, MaxTicksPerRun,
            new[]
            {
                new ScenarioSide(new[] { sideA }),
                new ScenarioSide(new[] { sideB }),
            });

        private static PowerBand ResolveCell(int authoredTier, int authoredBand, System.Func<PowerBand> computeFallback) =>
            authoredBand >= 1 ? new PowerBand(authoredTier, authoredBand) : computeFallback();

        private static Dictionary<ScoreId, int> SumScores(
            IReadOnlyDictionary<ScoreId, int> a, IReadOnlyDictionary<ScoreId, int> b)
        {
            var result = new Dictionary<ScoreId, int>(a);
            foreach (var (score, value) in b)
                result[score] = result.TryGetValue(score, out var existing) ? existing + value : value;
            return result;
        }
    }
}
