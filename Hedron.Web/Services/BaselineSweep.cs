using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Systems;

namespace Hedron.Web.Services
{
    /// <summary>
    /// Composes the Standards page's "Re-run baseline sweep" scenario list (sim-3 Postcondition 10)
    /// — scenario <em>selection</em>, not verdict math: every composed scenario is validated and run
    /// through the untouched engine seams (<c>ISimScenarioStore.Validate</c>/<c>ISimOutcomeEvaluator</c>).
    /// One equal-cell scenario per (Tier, Band) cell, one adjacent-pair scenario per consecutive
    /// global-band-index pair. Today's one consumer lives web-side; a second consumer (e.g. a future
    /// <c>simulate --sweep</c>) would promote this into the Simulation module (recorded as a watch
    /// item, not built speculatively).
    /// </summary>
    public static class BaselineSweep
    {
        public const string PolicyId = "cooldown-first";
        public const int Seed = 1234;
        public const int Iterations = 200;
        public const int MaxTicksPerRun = 100;

        public static IReadOnlyList<ScenarioDefinition> Compose(PowerBudgetTunables tunables)
        {
            var cells = new List<(int Tier, int Band)>();
            for (var tier = 0; tier <= tunables.MaxTier; tier++)
                for (var band = 1; band <= tunables.BandsPerTier; band++)
                    cells.Add((tier, band));

            var scenarios = new List<ScenarioDefinition>();
            foreach (var cell in cells)
                scenarios.Add(EqualCellScenario(cell.Tier, cell.Band));

            var ordered = cells.OrderBy(c => tunables.GlobalBandIndex(c.Tier, c.Band)).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
                scenarios.Add(AdjacentPairScenario(ordered[i], ordered[i + 1]));

            return scenarios;
        }

        private static ScenarioDefinition EqualCellScenario(int tier, int band) => new(
            ScenarioKind.Combat, $"sweep.equal.t{tier}b{band}", Seed, Iterations, MaxTicksPerRun,
            new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: tier, Band: band) }),
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: tier, Band: band) }),
            });

        private static ScenarioDefinition AdjacentPairScenario((int Tier, int Band) a, (int Tier, int Band) b) => new(
            ScenarioKind.Combat, $"sweep.adjacent.t{a.Tier}b{a.Band}-vs-t{b.Tier}b{b.Band}", Seed, Iterations, MaxTicksPerRun,
            new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: a.Tier, Band: a.Band) }),
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, PolicyId, Tier: b.Tier, Band: b.Band) }),
            });
    }
}
