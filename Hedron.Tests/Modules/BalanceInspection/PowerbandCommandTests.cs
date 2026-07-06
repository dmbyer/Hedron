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
    /// Coverage contract: docs/implementation-plans/power-budget-inspector.md P6 — list-all vs.
    /// single-tier output and the admin-gate declaration.
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

        private static PowerbandCommand MakeCommand() => new(new PowerBudgetSystem());

        [Fact]
        public void RequiredPrivileges_contains_AdminRequirement()
        {
            var cmd = new PowerbandCommand(null!);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
        }

        [Fact]
        public async Task No_argument_lists_every_band_zero_through_max_tier_with_anchors()
        {
            var command = MakeCommand();
            var powerBudget = new PowerBudgetSystem();
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(null), output);

            await command.ExecuteAsync(ctx);

            var message = Assert.Single(GetMessages(output));
            Assert.Equal(PowerBudgetConstants.MaxTier + 1, message.Rows.Count);

            for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
            {
                var row = message.Rows[tier];
                Assert.Equal(tier, row.Tier);
                Assert.Equal(powerBudget.BandAnchor(tier), row.Anchor);
            }
        }

        [Fact]
        public async Task Tier_argument_returns_a_single_row_with_anchor_and_reference_estimate()
        {
            var command = MakeCommand();
            var powerBudget = new PowerBudgetSystem();
            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs("3"), output);

            await command.ExecuteAsync(ctx);

            var message = Assert.Single(GetMessages(output));
            var row = Assert.Single(message.Rows);
            Assert.Equal(3, row.Tier);
            Assert.Equal(powerBudget.BandAnchor(3), row.Anchor);
            Assert.Equal(
                powerBudget.Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores), 3),
                row.ReferenceEstimate);
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
