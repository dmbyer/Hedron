using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.BalanceInspection.Commands;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 2 — handler/command tests for <see cref="PowerbandCommand"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md — the ~21-cell
    /// (Tier, Band) listing vs. single-tier (three cells) output and the admin-gate declaration.
    /// </summary>
    public sealed class PowerbandCommandTests
    {
        private static ParsedArguments MakeArgs(string? tier)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;

            var values = new Dictionary<string, object?>();
            if (tier is not null)
                values["tier"] = tier;

            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(1u);
            return new CommandContext(session, 1u, args, output.WriterFor(1u), Services: null!);
        }

        private static PowerbandCommand MakeCommand() =>
            new(new PowerBudgetSystem(PowerBudgetTunables.Default), PowerBudgetTunables.Default);

        [Fact]
        public void RequiredPrivileges_contains_AdminRequirement()
        {
            var cmd = new PowerbandCommand(null!, PowerBudgetTunables.Default);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
        }

        [Fact]
        public async Task No_argument_lists_every_tier_and_band_cell_with_target_ranges()
        {
            var command = MakeCommand();
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(null), output);

            await command.ExecuteAsync(ctx);

            var message = Assert.Single(GetMessages(output));
            Assert.Equal((PowerBudgetTunables.Default.MaxTier + 1) * PowerBudgetTunables.Default.BandsPerTier, message.Rows.Count);

            var index = 0;
            for (var tier = 0; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
            {
                for (var band = 1; band <= PowerBudgetTunables.Default.BandsPerTier; band++)
                {
                    var row = message.Rows[index++];
                    Assert.Equal(tier, row.Tier);
                    Assert.Equal(band, row.Band);
                    Assert.Equal(powerBudget.TargetRange(tier, band), row.Range);
                }
            }
        }

        [Fact]
        public async Task Tier_argument_returns_that_tiers_three_band_rows()
        {
            var command = MakeCommand();
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs("3"), output);

            await command.ExecuteAsync(ctx);

            var message = Assert.Single(GetMessages(output));
            Assert.Equal(PowerBudgetTunables.Default.BandsPerTier, message.Rows.Count);

            for (var band = 1; band <= PowerBudgetTunables.Default.BandsPerTier; band++)
            {
                var row = message.Rows[band - 1];
                Assert.Equal(3, row.Tier);
                Assert.Equal(band, row.Band);
                Assert.Equal(powerBudget.TargetRange(3, band), row.Range);
            }
        }

        [Fact]
        public async Task Row_count_reflects_injected_non_default_MaxTier_and_BandsPerTier()
        {
            // A synthetic tunables record with genuinely different table bounds — proves the row
            // count/table shape derives from the injected instance, not PowerBudgetTunables.Default.
            var custom = PowerBudgetTunables.Default with { MaxTier = 2, BandsPerTier = 2 };
            var command = new PowerbandCommand(new PowerBudgetSystem(custom), custom);
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(null), output);

            await command.ExecuteAsync(ctx);

            var message = Assert.Single(GetMessages(output));
            Assert.Equal((custom.MaxTier + 1) * custom.BandsPerTier, message.Rows.Count);
            Assert.NotEqual((PowerBudgetTunables.Default.MaxTier + 1) * PowerBudgetTunables.Default.BandsPerTier, message.Rows.Count);
        }

        [Theory]
        [InlineData("7")]
        [InlineData("-1")]
        [InlineData("abc")]
        public async Task Invalid_tier_argument_writes_error_and_no_powerband_message(string tier)
        {
            var command = MakeCommand();
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(tier), output);

            await command.ExecuteAsync(ctx);

            Assert.Empty(GetMessages(output));
            var error = Assert.Single(GetPlainMessages(output));
            Assert.Equal(OutputSeverity.Error, error.Severity);
        }

        private static List<PowerbandMessage> GetMessages(RecordingOutput output)
        {
            var result = new List<PowerbandMessage>();
            foreach (var (type, _, message) in output.All)
                if (type == typeof(PowerbandMessage))
                    result.Add((PowerbandMessage)message);
            return result;
        }

        private static List<PlainMessage> GetPlainMessages(RecordingOutput output)
        {
            var result = new List<PlainMessage>();
            foreach (var (type, _, message) in output.All)
                if (type == typeof(PlainMessage))
                    result.Add((PlainMessage)message);
            return result;
        }
    }
}
