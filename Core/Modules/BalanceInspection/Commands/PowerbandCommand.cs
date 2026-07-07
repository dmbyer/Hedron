using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Commands
{
    /// <summary>
    /// Admin/designer command <c>powerband [tier]</c>. With no argument, lists every tier band's
    /// lower anchor (0–<see cref="PowerBudgetConstants.MaxTier"/>); with a tier argument, prints that
    /// band's anchor and the reference base build's power at that tier.
    /// </summary>
    public sealed class PowerbandCommand : ICommand
    {
        private readonly IPowerBudgetSystem _powerBudget;

        public string Name => "powerband";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "List tier power-band anchors.";
        public string LongDescription =>
            "With no argument, lists every tier band (0-6) with its lower power anchor. " +
            "With a tier argument, prints that band's anchor and the reference base build's power at that tier.";
        public string Usage => "powerband [tier]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("tier", typeof(string), CommandArgumentKind.Token,
                Required: false, "Tier (0-6) to inspect (omit to list every band)."),
        });

        public PowerbandCommand(IPowerBudgetSystem powerBudget)
        {
            _powerBudget = powerBudget;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("tier", out var tierArg);

            if (string.IsNullOrWhiteSpace(tierArg))
            {
                var rows = new List<PowerBandRow>(PowerBudgetConstants.MaxTier + 1);
                for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
                    rows.Add(BuildRow(tier));

                await context.Output.WriteAsync(new PowerbandMessage(rows)).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(tierArg, out var requestedTier) ||
                requestedTier < 0 || requestedTier > PowerBudgetConstants.MaxTier)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Tier must be an integer 0-{PowerBudgetConstants.MaxTier}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PowerbandMessage(new[] { BuildRow(requestedTier) }))
                .ConfigureAwait(false);
        }

        private PowerBandRow BuildRow(int tier)
        {
            var anchor = _powerBudget.BandAnchor(tier);
            var referenceEstimate = _powerBudget.Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores), tier);
            return new PowerBandRow(tier, anchor, referenceEstimate);
        }
    }
}
