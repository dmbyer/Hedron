using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 3 — <see cref="SimulateRunMode"/> exit-code contract + report artifact (Postcondition 1),
    /// mirroring <c>GenerationRunModeTests</c>.
    /// </summary>
    public sealed class SimulateRunModeTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-simrun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        private static IConfiguration ConfigFor(string reportDirectory) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Simulation:ReportDirectory"] = reportDirectory,
                })
                .Build();

        private string WriteScenario(string body)
        {
            var dir = NewTempDir();
            var path = Path.Combine(dir, "scenario.yaml");
            File.WriteAllText(path, body);
            return path;
        }

        private const string ValidScenario = """
            kind: Combat
            name: run-mode-probe
            seed: 5
            iterations: 10
            maxTicksPerRun: 50
            sides:
              - combatants:
                  - source: ReferenceBuild
                    tier: 2
                    band: 2
                    policyId: cooldown-first
              - combatants:
                  - source: ReferenceBuild
                    tier: 2
                    band: 2
                    policyId: cooldown-first
            """;

        [Fact]
        public async Task RunAsync_ValidScenario_ExitsZeroAndWritesReport()
        {
            var scenarioPath = WriteScenario(ValidScenario);
            var reportDir = NewTempDir();

            var exit = await SimulateRunMode.RunAsync(
                new[] { "simulate", "--scenario", scenarioPath }, ConfigFor(reportDir));

            Assert.Equal(0, exit);
            Assert.Single(Directory.EnumerateFiles(reportDir, "*.json"));
        }

        [Fact]
        public async Task RunAsync_SeedOverride_IsHonored()
        {
            var scenarioPath = WriteScenario(ValidScenario);
            var reportDir = NewTempDir();

            var exit = await SimulateRunMode.RunAsync(
                new[] { "simulate", "--scenario", scenarioPath, "--seed", "999" }, ConfigFor(reportDir));

            Assert.Equal(0, exit);
            var reportFile = Directory.EnumerateFiles(reportDir, "*.json").Single();
            Assert.Contains("-999.json", reportFile);
        }

        [Fact]
        public async Task RunAsync_StructurallyInvalidScenario_ExitsTwo()
        {
            var scenarioPath = WriteScenario(ValidScenario.Replace("kind: Combat", "kind: NotAKind"));
            var exit = await SimulateRunMode.RunAsync(
                new[] { "simulate", "--scenario", scenarioPath }, ConfigFor(NewTempDir()));

            Assert.Equal(2, exit);
        }

        [Fact]
        public async Task RunAsync_MissingScenarioFile_ExitsTwo()
        {
            var missing = Path.Combine(NewTempDir(), "does-not-exist.yaml");
            var exit = await SimulateRunMode.RunAsync(
                new[] { "simulate", "--scenario", missing }, ConfigFor(NewTempDir()));

            Assert.Equal(2, exit);
        }

        [Fact]
        public async Task RunAsync_MissingScenarioArg_ExitsTwo()
        {
            var exit = await SimulateRunMode.RunAsync(new[] { "simulate" }, ConfigFor(NewTempDir()));
            Assert.Equal(2, exit);
        }

        [Fact]
        public void Matches_RecognizesSimulateToken_Only()
        {
            Assert.True(SimulateRunMode.Matches(new[] { "simulate", "--scenario", "p" }));
            Assert.False(SimulateRunMode.Matches(Array.Empty<string>()));
            Assert.False(SimulateRunMode.Matches(new[] { "generate" }));
        }
    }
}
