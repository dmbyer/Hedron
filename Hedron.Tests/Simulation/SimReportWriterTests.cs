using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 4 — <see cref="SimReportWriter"/> JSON round-trip + atomic write (Postcondition 9).</summary>
    public sealed class SimReportWriterTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-sim-reports-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        private SimReportWriter NewWriter() => new(Options.Create(new SimulationOptions { ReportDirectory = _tempDir }));

        private static SimulationReport SampleReport(string name = "probe", int seed = 42) => new(
            SchemaVersion: 1,
            Scenario: new ScenarioDefinition(
                ScenarioKind.Combat, name, seed, Iterations: 10, MaxTicksPerRun: 50,
                Sides: new[]
                {
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 2, Band: 2) }),
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 2, Band: 2) }),
                }),
            GeneratedAt: new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
            SideAWins: 6, SideBWins: 4, Draws: 0,
            SideAWinRate: 0.6, SideBWinRate: 0.4,
            TicksToKill: new DistributionStats(10, 10, 8, 12, 5, 15),
            SideADamageDealt: new DistributionStats(50, 50, 40, 60, 30, 70),
            SideBDamageDealt: new DistributionStats(45, 45, 35, 55, 25, 65),
            Verdicts: new[] { new SimVerdict("equalCellWinRate", true, "60% vs expected 50% ± 10%") });

        [Fact]
        public async Task WriteAsync_ThenReread_RoundTripsSchemaVersionAndAggregates()
        {
            var writer = NewWriter();
            var report = SampleReport();

            var path = await writer.WriteAsync(report);
            var body = await File.ReadAllTextAsync(path);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            };
            var reread = JsonSerializer.Deserialize<SimulationReport>(body, options);

            Assert.NotNull(reread);
            Assert.Equal(report.SchemaVersion, reread!.SchemaVersion);
            Assert.Equal(report.SideAWins, reread.SideAWins);
            Assert.Equal(report.SideBWins, reread.SideBWins);
            Assert.Equal(report.Draws, reread.Draws);
            Assert.Equal(report.TicksToKill, reread.TicksToKill);
            Assert.Equal(report.SideADamageDealt, reread.SideADamageDealt);
            Assert.Equal(report.SideBDamageDealt, reread.SideBDamageDealt);
            Assert.Equal(report.Scenario.Name, reread.Scenario.Name);
            Assert.Equal(report.Scenario.Seed, reread.Scenario.Seed);
            Assert.Single(reread.Verdicts);
            Assert.Equal("equalCellWinRate", reread.Verdicts[0].Name);
        }

        [Fact]
        public async Task WriteAsync_LeavesNoTmpFile()
        {
            var writer = NewWriter();
            await writer.WriteAsync(SampleReport());

            Assert.Empty(Directory.EnumerateFiles(_tempDir, "*.tmp"));
        }

        [Fact]
        public async Task WriteAsync_TwoWrites_ProduceTwoFiles()
        {
            var writer = NewWriter();

            var pathA = await writer.WriteAsync(SampleReport(name: "probe-a"));
            var pathB = await writer.WriteAsync(SampleReport(name: "probe-b"));

            Assert.NotEqual(pathA, pathB);
            Assert.Equal(2, Directory.EnumerateFiles(_tempDir, "*.json").Count());
        }
    }
}
