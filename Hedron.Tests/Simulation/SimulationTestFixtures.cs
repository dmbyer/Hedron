using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Shared, real (non-mock) wiring for <see cref="ISimulationRunner"/>-level tests — the mob
    /// catalog is never touched by a <see cref="CombatantSourceKind.ReferenceBuild"/>-only scenario,
    /// so a throw-on-use stub is enough.
    /// </summary>
    internal static class SimulationTestFixtures
    {
        private sealed class UnusedContentCatalog : IContentDefinitionCatalog
        {
            public IReadOnlyList<ContentSummary> List(ContentKind kind) => throw new NotImplementedException();
            public IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId) => throw new NotImplementedException();
            public ContentDefinition? Load(ContentKind kind, string blueprintId) => throw new NotImplementedException();
            public Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentWriteResult> SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentDeleteResult> DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentRenameResult> RenameAsync(ContentKind kind, string oldId, string newId, CancellationToken ct = default) => throw new NotImplementedException();
            public ContentDefinition CreateNew(ContentKind kind, string name) => throw new NotImplementedException();
            public ContentDefinition CreateNew(ContentKind kind, string name, string? blueprintId) => throw new NotImplementedException();
            public Task<ContentWriteResult> CreateAsync(ContentDefinition definition, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentWriteResult> RemoveRoomExitAsync(string roomBlueprintId, Direction direction, bool bidirectional, CancellationToken ct = default) => throw new NotImplementedException();
            public ContentDefinition WithBlueprintId(ContentDefinition definition, string? blueprintId) => throw new NotImplementedException();
            public ContentDefinition CreateNextFrom(ContentDefinition previous, string name) => throw new NotImplementedException();
            public void Invalidate() { }
        }

        public static ISimulationRunner NewRunner(FakeClock clock)
        {
            var abilityRegistry = new AbilityRegistry();
            var standardsRegistry = new BalanceStandardsRegistry(BalanceStandardsDefaults.Document);
            var combatantFactory = new SimCombatantFactory(new UnusedContentCatalog(), standardsRegistry, abilityRegistry);
            var sandboxFactory = new SandboxWorldFactory(
                abilityRegistry, new EffectRegistry(), new PowerBudgetSystem(PowerBudgetTunables.Default),
                Options.Create(new DeathOptions()));
            var policies = new ISimCombatantPolicy[]
            {
                new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(abilityRegistry),
            };
            var evaluator = new SimOutcomeEvaluator(standardsRegistry);

            return new SimulationRunner(combatantFactory, sandboxFactory, policies, evaluator, clock);
        }

        public static ScenarioDefinition ReferenceBuildScenario(
            string name, int seed, int iterations, int maxTicksPerRun,
            int tierA, int bandA, int tierB, int bandB, string policyId = "cooldown-first") => new(
            ScenarioKind.Combat, name, seed, iterations, maxTicksPerRun,
            new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, policyId, Tier: tierA, Band: bandA) }),
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, policyId, Tier: tierB, Band: bandB) }),
            });

        /// <summary>Subject = side 0, victim = side 1 — both <see cref="CombatantSourceKind.ReferenceBuild"/>, policy id omitted (unused by this kind).</summary>
        public static ScenarioDefinition ProgressionScenario(
            string name, int seed, int iterations, int maxKillsPerRun,
            ScoreId targetTrack, int targetImprovements,
            int subjectTier, int subjectBand, int victimTier, int victimBand,
            double? ticksPerKill = null) => new(
            ScenarioKind.ProgressionRate, name, seed, iterations, MaxTicksPerRun: 1,
            Sides: new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: subjectTier, Band: subjectBand) }),
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: victimTier, Band: victimBand) }),
            },
            Progression: new ProgressionSettings(targetTrack, targetImprovements, maxKillsPerRun, ticksPerKill));
    }
}
