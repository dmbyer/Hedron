using System;
using System.Threading.Tasks;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Server
{
    /// <summary>
    /// Headless <c>simulate</c> run-mode: a no-chain Initiator (INV-10) that composes the engine's
    /// DI (no gameplay hosted services — no telnet/heartbeat/world-spawn), loads + validates one
    /// scenario, runs it via <see cref="ISimulationRunner.Run"/>, writes the JSON report artifact,
    /// prints a console summary, and exits 0 (clean run) / 1 (engine failure) / 2 (usage or
    /// scenario-invalid). Mirrors <see cref="GenerationRunMode"/>.
    /// </summary>
    public static class SimulateRunMode
    {
        /// <summary>Recognizes the run-mode token as the first CLI argument.</summary>
        public static bool Matches(string[] args) => args.Length > 0 && args[0] == "simulate";

        public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
        {
            string? scenarioPath = null;
            int? seedOverride = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--scenario" when i + 1 < args.Length:
                        scenarioPath = args[++i];
                        break;
                    case "--seed" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out var seed))
                        {
                            Console.Error.WriteLine($"simulate: --seed must be an integer, got '{args[i]}'.");
                            return 2;
                        }
                        seedOverride = seed;
                        break;
                    default:
                        Console.Error.WriteLine($"simulate: unrecognized argument '{args[i]}'.");
                        PrintUsage();
                        return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(scenarioPath))
            {
                Console.Error.WriteLine("simulate: --scenario <path> is required.");
                PrintUsage();
                return 2;
            }

            // Compose DI only — no gameplay hosted services (no telnet/heartbeat/world spawn).
            // AddLogging is required here (unlike GenerationRunMode): ISimCombatantFactory resolves
            // the real IContentDefinitionCatalog for the mob-template combatant source, whose
            // deserializers take ILogger<T> by constructor injection.
            var services = new ServiceCollection();
            services.AddLogging();
            services.Register(configuration);
            await using var provider = services.BuildServiceProvider();

            var store = provider.GetRequiredService<ISimScenarioStore>();

            ScenarioDefinition scenario;
            try
            {
                scenario = store.Load(scenarioPath, seedOverride);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"simulate: failed to load scenario '{scenarioPath}': {ex.Message}");
                return 2;
            }

            var runner = provider.GetRequiredService<ISimulationRunner>();

            SimulationReport report;
            try
            {
                report = runner.Run(scenario);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"simulate: run failed: {ex.Message}");
                return 1;
            }

            var writer = provider.GetRequiredService<ISimReportWriter>();
            var reportPath = await writer.WriteAsync(report);

            PrintSummary(report, reportPath);

            return 0;
        }

        private static void PrintSummary(SimulationReport report, string reportPath)
        {
            Console.WriteLine("Simulation run summary");
            Console.WriteLine($"  scenario:   {report.Scenario.Name}");
            Console.WriteLine($"  seed:       {report.Scenario.Seed}");
            Console.WriteLine($"  iterations: {report.Scenario.Iterations}");
            Console.WriteLine($"  side A wins: {report.SideAWins} ({report.SideAWinRate:P1})");
            Console.WriteLine($"  side B wins: {report.SideBWins} ({report.SideBWinRate:P1})");
            Console.WriteLine($"  draws:       {report.Draws}");
            Console.WriteLine(
                $"  ticks-to-kill: mean {report.TicksToKill.Mean:F1}, median {report.TicksToKill.Median:F1}, " +
                $"p10 {report.TicksToKill.P10:F1}, p90 {report.TicksToKill.P90:F1}");
            Console.WriteLine("  verdicts:");
            foreach (var verdict in report.Verdicts)
            {
                var status = verdict.Passed switch { true => "PASS", false => "FAIL", null => "SKIP" };
                Console.WriteLine($"    [{status}] {verdict.Name}: {verdict.Reason}");
            }
            Console.WriteLine($"  report: {reportPath}");
        }

        private static void PrintUsage() =>
            Console.Error.WriteLine("usage: dotnet run --project Server -- simulate --scenario <path> [--seed N]");
    }
}
