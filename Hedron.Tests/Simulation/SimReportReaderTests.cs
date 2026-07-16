using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 1 — <see cref="SimReportReader"/> list + read (Postcondition 6).</summary>
    public sealed class SimReportReaderTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "hedron-sim-reports-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        private SimReportWriter NewWriter() => new(Options.Create(new SimulationOptions { ReportDirectory = _tempDir }));
        private SimReportReader NewReader() => new(Options.Create(new SimulationOptions { ReportDirectory = _tempDir }));

        private static SimulationReport SampleReport(string name, DateTime generatedAt, int seed = 42) => new(
            SchemaVersion: 1,
            Scenario: new ScenarioDefinition(
                ScenarioKind.Combat, name, seed, Iterations: 10, MaxTicksPerRun: 50,
                Sides: new[]
                {
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 2, Band: 2) }),
                    new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 2, Band: 2) }),
                }),
            GeneratedAt: generatedAt,
            SideAWins: 6, SideBWins: 4, Draws: 0,
            SideAWinRate: 0.6, SideBWinRate: 0.4,
            TicksToKill: new DistributionStats(10, 10, 8, 12, 5, 15),
            SideADamageDealt: new DistributionStats(50, 50, 40, 60, 30, 70),
            SideBDamageDealt: new DistributionStats(45, 45, 35, 55, 25, 65),
            Verdicts: new[] { new SimVerdict("equalCellWinRate", true, "60% vs expected 50% ± 10%") });

        [Fact]
        public async Task Read_WriterWrittenFile_RoundTripsSchemaVersionAggregatesAndVerdicts()
        {
            var writer = NewWriter();
            var reader = NewReader();
            var report = SampleReport("probe", new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));
            var path = await writer.WriteAsync(report);

            var reread = reader.Read(path);

            Assert.Equal(report.SchemaVersion, reread.SchemaVersion);
            Assert.Equal(report.SideAWins, reread.SideAWins);
            Assert.Equal(report.SideBWins, reread.SideBWins);
            Assert.Equal(report.Draws, reread.Draws);
            Assert.Equal(report.TicksToKill, reread.TicksToKill);
            Assert.Single(reread.Verdicts);
            Assert.Equal("equalCellWinRate", reread.Verdicts[0].Name);
            Assert.Equal(true, reread.Verdicts[0].Passed);
        }

        [Fact]
        public async Task List_OrdersNewestFirst()
        {
            var writer = NewWriter();
            var reader = NewReader();
            await writer.WriteAsync(SampleReport("older", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));
            await writer.WriteAsync(SampleReport("newer", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));

            var summaries = reader.List();

            Assert.Equal(2, summaries.Count);
            Assert.Equal("newer", summaries[0].ScenarioName);
            Assert.Equal("older", summaries[1].ScenarioName);
        }

        [Fact]
        public async Task List_FlagsUnreadableFileRatherThanThrowing()
        {
            var writer = NewWriter();
            var reader = NewReader();
            await writer.WriteAsync(SampleReport("good", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));
            var badPath = Path.Combine(_tempDir, "corrupt.json");
            await File.WriteAllTextAsync(badPath, "{ not valid json");

            var summaries = reader.List();

            Assert.Equal(2, summaries.Count);
            var bad = summaries.Single(s => s.Path == badPath);
            Assert.False(bad.Readable);
            Assert.NotNull(bad.Error);
            var good = summaries.Single(s => s.Path != badPath);
            Assert.True(good.Readable);
        }

        [Fact]
        public void List_MissingDirectory_ReturnsEmpty()
        {
            var reader = NewReader();
            Assert.Empty(reader.List());
        }

        [Fact]
        public async Task Read_ToleratesUnknownAdditiveJsonFields()
        {
            var writer = NewWriter();
            var reader = NewReader();
            var path = await writer.WriteAsync(SampleReport("probe", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));

            var body = await File.ReadAllTextAsync(path);
            var withExtraField = body.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": 123\n}";
            await File.WriteAllTextAsync(path, withExtraField);

            var reread = reader.Read(path);

            Assert.Equal(1, reread.SchemaVersion);
        }

        [Fact]
        public async Task Read_PreSim4CombatReportJsonWithNoProgressionRateProperty_StillDeserializes()
        {
            // Simulates a sim-3-era artifact written before the additive "progressionRate"
            // property existed (Postcondition 5) — strip the property entirely, not just null it.
            var writer = NewWriter();
            var reader = NewReader();
            var path = await writer.WriteAsync(SampleReport("probe", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));

            var body = await File.ReadAllTextAsync(path);
            var node = JsonNode.Parse(body)!.AsObject();
            node.Remove("progressionRate");
            await File.WriteAllTextAsync(path, node.ToJsonString());

            var reread = reader.Read(path);

            Assert.Equal(1, reread.SchemaVersion);
            Assert.Null(reread.ProgressionRate);
            Assert.Equal(6, reread.SideAWins);
        }

        [Fact]
        public async Task Read_ProgressionReport_RoundTripsProgressionRatePayload()
        {
            var writer = NewWriter();
            var reader = NewReader();
            var report = new SimulationReport(
                SchemaVersion: 1,
                Scenario: new ScenarioDefinition(
                    ScenarioKind.ProgressionRate, "probe-progression", Seed: 42, Iterations: 10, MaxTicksPerRun: 1,
                    Sides: new[]
                    {
                        new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: 2, Band: 2) }),
                        new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, string.Empty, Tier: 2, Band: 2) }),
                    },
                    Progression: new ProgressionSettings(ScoreId.Body, TargetImprovements: 2, MaxKillsPerRun: 200)),
                GeneratedAt: new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
                SideAWins: 0, SideBWins: 0, Draws: 0,
                SideAWinRate: 0.0, SideBWinRate: 0.0,
                TicksToKill: new DistributionStats(0, 0, 0, 0, 0, 0),
                SideADamageDealt: new DistributionStats(0, 0, 0, 0, 0, 0),
                SideBDamageDealt: new DistributionStats(0, 0, 0, 0, 0, 0),
                Verdicts: new[] { new SimVerdict("targetReached", true, "10/10 runs reached the target before the cap (100.0 %)") },
                ProgressionRate: new ProgressionRateResult(
                    ScoreId.Body, TargetImprovements: 2, RunsReachedTarget: 10,
                    KillsToTarget: new DistributionStats(15, 15, 12, 18, 10, 20),
                    MeanMilestoneKills: new[] { 10.0, 15.0 },
                    Tracks: new[] { new ProgressionTrackResult(ScoreId.Body, new DistributionStats(150, 150, 120, 180, 100, 200), new DistributionStats(2, 2, 2, 2, 2, 2)) },
                    TicksPerKill: null, TicksToTarget: null));
            var path = await writer.WriteAsync(report);

            var reread = reader.Read(path);

            Assert.NotNull(reread.ProgressionRate);
            Assert.Equal(ScoreId.Body, reread.ProgressionRate!.TargetTrack);
            Assert.Equal(10, reread.ProgressionRate.RunsReachedTarget);
            Assert.Null(reread.ProgressionRate.TicksToTarget);
            Assert.Single(reread.Verdicts);
        }
    }
}
