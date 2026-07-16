using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Progression;
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

        public SimulationReport Run(
            ScenarioDefinition scenario,
            int? maxParallelism = null,
            CancellationToken cancellationToken = default,
            Action? onRunCompleted = null) => scenario.Kind switch
        {
            ScenarioKind.Combat => RunCombat(scenario, maxParallelism, cancellationToken, onRunCompleted),
            ScenarioKind.ProgressionRate => RunProgressionRate(scenario, maxParallelism, cancellationToken, onRunCompleted),
            _ => throw new NotSupportedException($"scenario kind '{scenario.Kind}' has no executor."),
        };

        private SimulationReport RunCombat(
            ScenarioDefinition scenario,
            int? maxParallelism,
            CancellationToken cancellationToken,
            Action? onRunCompleted)
        {
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
                CancellationToken = cancellationToken,
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
                onRunCompleted?.Invoke();
            });

            return ReduceCombat(scenario, resolvedA, resolvedB, runRecords);
        }

        private SimulationReport ReduceCombat(
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

        private SimulationReport RunProgressionRate(
            ScenarioDefinition scenario,
            int? maxParallelism,
            CancellationToken cancellationToken,
            Action? onRunCompleted)
        {
            var settings = scenario.Progression
                ?? throw new InvalidOperationException("progressionRate scenario has no 'progression' section (should have failed ISimScenarioStore.Validate).");

            var subjectSpec = scenario.Sides[0].Combatants[0];
            var victimSpec = scenario.Sides[1].Combatants[0];

            // Pre-resolution — once per scenario, never per run (the hot path does no file/registry I/O).
            var resolvedSubject = _combatantFactory.Resolve(subjectSpec);
            var resolvedVictim = _combatantFactory.Resolve(victimSpec);

            var runRecords = new ProgressionRunRecord[scenario.Iterations];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism ?? Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            };

            Parallel.For(0, scenario.Iterations, parallelOptions, i =>
            {
                var runSeed = SimSeeds.DeriveRunSeed(scenario.Seed, i);
                var random = new SeededRandom(runSeed);
                var world = _sandboxWorldFactory.Create(random);

                var subjectId = _combatantFactory.Materialize(world, resolvedSubject);
                var victimId = _combatantFactory.Materialize(world, resolvedVictim);

                var executor = new ProgressionScenarioExecutor();
                runRecords[i] = executor.ExecuteRun(world, subjectId, victimId, settings, i);
                onRunCompleted?.Invoke();
            });

            return ReduceProgression(scenario, settings, runRecords);
        }

        private SimulationReport ReduceProgression(
            ScenarioDefinition scenario, ProgressionSettings settings, ProgressionRunRecord[] runRecords)
        {
            var reached = runRecords.Where(r => r.ReachedTarget).ToList();

            var killsToTarget = DistributionStats.From(reached.Select(r => r.Kills).ToList());

            // Per milestone index, average over whichever runs actually reached that milestone —
            // early milestones have more data than the final one (fewer runs make it that far).
            var meanMilestoneKills = new List<double>(settings.TargetImprovements);
            for (var m = 0; m < settings.TargetImprovements; m++)
            {
                var atMilestone = runRecords.Where(r => r.MilestoneKills.Count > m).Select(r => (double)r.MilestoneKills[m]).ToList();
                meanMilestoneKills.Add(atMilestone.Count > 0 ? atMilestone.Average() : 0.0);
            }

            var trackResults = ProgressionConstants.CombatTracks.Select(track => new ProgressionTrackResult(
                track,
                DistributionStats.From(runRecords.Select(r => r.FinalXp[track]).ToList()),
                DistributionStats.From(runRecords.Select(r => r.FinalImprovements[track]).ToList()))).ToList();

            DistributionStats? ticksToTarget = settings.TicksPerKill is { } ticksPerKill
                ? DistributionStats.From(reached.Select(r => (int)Math.Round(r.Kills * ticksPerKill, MidpointRounding.AwayFromZero)).ToList())
                : null;

            var progressionResult = new ProgressionRateResult(
                settings.TargetTrack,
                settings.TargetImprovements,
                reached.Count,
                killsToTarget,
                meanMilestoneKills,
                trackResults,
                settings.TicksPerKill,
                ticksToTarget);

            var verdicts = _outcomeEvaluator.EvaluateProgressionRate(reached.Count, runRecords.Length);
            var empty = DistributionStats.From(Array.Empty<int>());

            return new SimulationReport(
                SchemaVersion: 1,
                Scenario: scenario,
                GeneratedAt: _clock.UtcNow,
                SideAWins: 0,
                SideBWins: 0,
                Draws: 0,
                SideAWinRate: 0.0,
                SideBWinRate: 0.0,
                TicksToKill: empty,
                SideADamageDealt: empty,
                SideBDamageDealt: empty,
                Verdicts: verdicts,
                ProgressionRate: progressionResult);
        }
    }
}
