using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Precondition: <paramref name="scenario"/> already passed <see cref="ISimScenarioStore.Validate"/>
    /// (the run-mode/caller's job, per the Main flow) — this type does not re-validate structure.
    /// </summary>
    public sealed class SimulationRunner : ISimulationRunner
    {
        private readonly ISimCombatantFactory _combatantFactory;
        private readonly ISandboxWorldFactory _sandboxWorldFactory;
        private readonly IEnumerable<ISimCombatantPolicy> _policies;
        private readonly ISimOutcomeEvaluator _outcomeEvaluator;
        private readonly IClock _clock;

        public SimulationRunner(
            ISimCombatantFactory combatantFactory,
            ISandboxWorldFactory sandboxWorldFactory,
            IEnumerable<ISimCombatantPolicy> policies,
            ISimOutcomeEvaluator outcomeEvaluator,
            IClock clock)
        {
            _combatantFactory = combatantFactory;
            _sandboxWorldFactory = sandboxWorldFactory;
            _policies = policies;
            _outcomeEvaluator = outcomeEvaluator;
            _clock = clock;
        }

        public SimulationReport Run(ScenarioDefinition scenario, int? maxParallelism = null)
        {
            if (scenario.Kind != ScenarioKind.Combat)
                throw new NotSupportedException(
                    $"scenario kind '{scenario.Kind}' has no executor yet (reserved for sim-4).");

            var sideASpec = scenario.Sides[0].Combatants[0];
            var sideBSpec = scenario.Sides[1].Combatants[0];

            // Pre-resolution — once per scenario, never per run (the hot path does no file/registry I/O).
            var resolvedA = _combatantFactory.Resolve(sideASpec);
            var resolvedB = _combatantFactory.Resolve(sideBSpec);

            var policiesById = new Dictionary<string, ISimCombatantPolicy>();
            foreach (var policy in _policies)
                policiesById[policy.PolicyId] = policy;

            var policyA = policiesById[sideASpec.PolicyId];
            var policyB = policiesById[sideBSpec.PolicyId];

            // Runs land in an array slot keyed by run index — the reduce below is index-ordered,
            // so the report is independent of scheduling under parallelism.
            var runRecords = new RunRecord[scenario.Iterations];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism ?? Environment.ProcessorCount,
            };

            Parallel.For(0, scenario.Iterations, parallelOptions, i =>
            {
                var runSeed = SimSeeds.DeriveRunSeed(scenario.Seed, i);
                var random = new SeededRandom(runSeed);
                var world = _sandboxWorldFactory.Create(random);

                var entityA = _combatantFactory.Materialize(world, resolvedA);
                var entityB = _combatantFactory.Materialize(world, resolvedB);

                var executor = new CombatScenarioExecutor();
                runRecords[i] = executor.ExecuteRun(world, entityA, entityB, policyA, policyB, scenario.MaxTicksPerRun, i);
            });

            return Reduce(scenario, resolvedA, resolvedB, runRecords);
        }

        private SimulationReport Reduce(
            ScenarioDefinition scenario, ResolvedCombatant resolvedA, ResolvedCombatant resolvedB, RunRecord[] runRecords)
        {
            var sideAWins = 0;
            var sideBWins = 0;
            var draws = 0;
            var ticks = new List<int>(runRecords.Length);
            var damageA = new List<int>(runRecords.Length);
            var damageB = new List<int>(runRecords.Length);

            foreach (var record in runRecords)
            {
                if (record.WinnerSide == 0) sideAWins++;
                else if (record.WinnerSide == 1) sideBWins++;
                else draws++;

                ticks.Add(record.Ticks);
                damageA.Add(record.SideADamageDealt);
                damageB.Add(record.SideBDamageDealt);
            }

            var decisive = sideAWins + sideBWins;
            var sideAWinRate = decisive > 0 ? (double)sideAWins / decisive : 0.0;
            var sideBWinRate = decisive > 0 ? (double)sideBWins / decisive : 0.0;

            var verdicts = _outcomeEvaluator.Evaluate(resolvedA, resolvedB, sideAWins, sideBWins, draws);

            return new SimulationReport(
                SchemaVersion: 1,
                Scenario: scenario,
                GeneratedAt: _clock.UtcNow,
                SideAWins: sideAWins,
                SideBWins: sideBWins,
                Draws: draws,
                SideAWinRate: sideAWinRate,
                SideBWinRate: sideBWinRate,
                TicksToKill: DistributionStats.From(ticks),
                SideADamageDealt: DistributionStats.From(damageA),
                SideBDamageDealt: DistributionStats.From(damageB),
                Verdicts: verdicts);
        }
    }
}
