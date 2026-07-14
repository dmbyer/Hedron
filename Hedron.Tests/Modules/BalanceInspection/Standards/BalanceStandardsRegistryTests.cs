using System;
using System.Collections.Generic;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="BalanceStandardsRegistry"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/balance-standards-registry.md Test plan
    /// item 8 — dense fill + composition (sparse cells fill with empty gear + global outcomes,
    /// ReferenceSnapshot = base + gear, OutcomesFor prefers the per-cell override).
    /// </summary>
    public sealed class BalanceStandardsRegistryTests
    {
        [Fact]
        public void Sparse_authored_cells_fill_with_empty_gear_and_global_outcomes()
        {
            var globalOutcomes = new OutcomeTolerances(0.5, 0.1, 0.65);
            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: 1, Outcomes: globalOutcomes,
                Cells: Array.Empty<BalanceStandard>());

            var registry = new BalanceStandardsRegistry(document);

            var cell = registry.Get(new PowerBand(3, 2));
            Assert.Empty(cell.ReferenceBuild.GearBonuses);
            Assert.Empty(cell.ReferenceBuild.AbilityKit);
            Assert.Null(cell.OutcomesOverride);
            Assert.Equal(globalOutcomes, registry.OutcomesFor(3, 2));
        }

        [Fact]
        public void AllIds_covers_every_cell_in_the_dense_table()
        {
            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: 1, Outcomes: BalanceStandardsDefaults.Outcomes,
                Cells: Array.Empty<BalanceStandard>());

            var registry = new BalanceStandardsRegistry(document);

            var expectedCount = (PowerBudgetTunables.Default.MaxTier + 1) * PowerBudgetTunables.Default.BandsPerTier;
            Assert.Equal(expectedCount, registry.AllIds.Count);
        }

        [Fact]
        public void ReferenceSnapshot_equals_reference_base_scores_plus_cell_gear_bonuses()
        {
            var tunables = PowerBudgetTunables.Default;
            var gear = new Dictionary<ScoreId, int> { [ScoreId.AttackPower] = 5 };
            var cell = new BalanceStandard(1, 2, new ReferenceBuildDefinition(gear, Array.Empty<string>()), null);
            var document = new BalanceStandardsDocument(
                tunables, BandDriftTolerance: 1, Outcomes: BalanceStandardsDefaults.Outcomes, Cells: new[] { cell });

            var registry = new BalanceStandardsRegistry(document);

            var snapshot = registry.ReferenceSnapshot(1, 2);
            Assert.Equal(tunables.ReferenceBaseScores[ScoreId.AttackPower] + 5, snapshot.Scores[ScoreId.AttackPower]);
            Assert.Equal(tunables.ReferenceBaseScores[ScoreId.Body], snapshot.Scores[ScoreId.Body]);
        }

        [Fact]
        public void OutcomesFor_prefers_the_per_cell_override_over_the_global_default()
        {
            var globalOutcomes = new OutcomeTolerances(0.5, 0.1, 0.65);
            var overrideOutcomes = new OutcomeTolerances(0.6, 0.2, 0.75);
            var cell = new BalanceStandard(
                0, 1, new ReferenceBuildDefinition(new Dictionary<ScoreId, int>(), Array.Empty<string>()), overrideOutcomes);
            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default, BandDriftTolerance: 1, Outcomes: globalOutcomes, Cells: new[] { cell });

            var registry = new BalanceStandardsRegistry(document);

            Assert.Equal(overrideOutcomes, registry.OutcomesFor(0, 1));
            Assert.Equal(globalOutcomes, registry.OutcomesFor(0, 2));
        }
    }
}
