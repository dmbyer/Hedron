using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Web
{
    /// <summary>Tier 1 — <see cref="SimulationPrefill"/> entry-point composition (sim-3 Postcondition 8).</summary>
    public sealed class SimulationPrefillTests
    {
        private static BalanceStandardsRegistry NewRegistry() => new(new BalanceStandardsDocument(
            PowerBudgetTunables.Default,
            BandDriftTolerance: 1,
            Outcomes: BalanceStandardsDefaults.Outcomes,
            Cells: new[]
            {
                new BalanceStandard(2, 2, new ReferenceBuildDefinition(
                    new Dictionary<ScoreId, int> { [ScoreId.AttackPower] = 6 }, new List<string> { "kick" }), null),
            }));

        private static ISimScenarioStore NewValidatingStore() =>
            new SimScenarioStore(
                new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) },
                Options.Create(new SimulationOptions()));

        [Fact]
        public void ForMob_BandedTemplate_PrefillsSideBWithAuthoredCellReferenceBuild()
        {
            var registry = NewRegistry();
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var mobProjection = new MobPowerProjectionSystem();
            var template = new MobTemplate("mob.test.banded") { Name = "guard", Body = 14, Tier = 2, Band = 2 };

            var scenario = SimulationPrefill.ForMob(template, powerBudget, mobProjection);

            var sideB = scenario.Sides[1].Combatants[0];
            Assert.Equal(CombatantSourceKind.ReferenceBuild, sideB.Source);
            Assert.Equal(2, sideB.Tier);
            Assert.Equal(2, sideB.Band);

            var sideA = scenario.Sides[0].Combatants[0];
            Assert.Equal(CombatantSourceKind.MobTemplate, sideA.Source);
            Assert.Equal(template.BlueprintId, sideA.MobBlueprintId);

            NewValidatingStore().Validate(scenario);
        }

        [Fact]
        public void ForMob_UnbandedTemplate_FallsBackToComputedCell()
        {
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var mobProjection = new MobPowerProjectionSystem();
            var template = new MobTemplate("mob.test.unbanded") { Name = "wisp", Body = 10, Tier = 0, Band = 0 };

            var expectedCell = powerBudget.Classify(powerBudget.Estimate(mobProjection.Project(template), template.Tier));

            var scenario = SimulationPrefill.ForMob(template, powerBudget, mobProjection);

            var sideB = scenario.Sides[1].Combatants[0];
            Assert.Equal(expectedCell.Tier, sideB.Tier);
            Assert.Equal(expectedCell.Band, sideB.Band);

            NewValidatingStore().Validate(scenario);
        }

        [Fact]
        public void ForItem_InlineScores_EqualPerScoreSumOfReferenceSnapshotAndItemProjection_AnnotatedWithItemCell()
        {
            var registry = NewRegistry();
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var itemProjection = new ItemPowerProjectionSystem();
            var template = new ItemTemplate("item.test.sword") { Name = "sword", Tier = 2, Band = 2 };
            template.StatBonuses.Add(new EquipmentStatBonus(ScoreId.AttackPower, 5));

            var scenario = SimulationPrefill.ForItem(template, registry, powerBudget, itemProjection);

            var sideA = scenario.Sides[0].Combatants[0];
            Assert.Equal(CombatantSourceKind.Inline, sideA.Source);
            Assert.Equal(2, sideA.Tier);
            Assert.Equal(2, sideA.Band);

            var expectedBaseline = registry.ReferenceSnapshot(2, 2);
            var expectedItem = itemProjection.Project(template);
            foreach (var (score, baseValue) in expectedBaseline.Scores)
            {
                var itemBonus = expectedItem.Scores.TryGetValue(score, out var bonus) ? bonus : 0;
                Assert.Equal(baseValue + itemBonus, sideA.Inline!.Scores[score]);
            }
            Assert.Equal(5, sideA.Inline!.Scores[ScoreId.AttackPower] - expectedBaseline.Scores[ScoreId.AttackPower]);

            var sideB = scenario.Sides[1].Combatants[0];
            Assert.Equal(CombatantSourceKind.ReferenceBuild, sideB.Source);
            Assert.Equal(2, sideB.Tier);
            Assert.Equal(2, sideB.Band);

            NewValidatingStore().Validate(scenario);
        }

        [Fact]
        public void ForItem_UnbandedTemplate_FallsBackToComputedCell()
        {
            var registry = NewRegistry();
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var itemProjection = new ItemPowerProjectionSystem();
            var template = new ItemTemplate("item.test.trinket") { Name = "trinket", Tier = 0, Band = 0 };
            template.StatBonuses.Add(new EquipmentStatBonus(ScoreId.Defense, 3));

            var expectedCell = powerBudget.Classify(powerBudget.Estimate(itemProjection.Project(template), template.Tier));

            var scenario = SimulationPrefill.ForItem(template, registry, powerBudget, itemProjection);

            var sideA = scenario.Sides[0].Combatants[0];
            Assert.Equal(expectedCell.Tier, sideA.Tier);
            Assert.Equal(expectedCell.Band, sideA.Band);

            NewValidatingStore().Validate(scenario);
        }
    }
}
