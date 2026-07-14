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
    /// Admin/designer command <c>powerband [tier]</c>. With no argument, lists every
    /// (Tier, Band) cell's target power range (0&#8211;<see cref="PowerBudgetTunables.MaxTier"/>
    /// × 1&#8211;<see cref="PowerBudgetTunables.BandsPerTier"/>, ~21 rows); with a tier argument,
    /// lists just that tier's <see cref="PowerBudgetTunables.BandsPerTier"/> rows. Table bounds
    /// come from the injected <see cref="PowerBudgetTunables"/> — data-backed, not a compiled
    /// constant.
    /// </summary>
    public sealed class PowerbandCommand : ICommand
    {
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly PowerBudgetTunables _tunables;

        public string Name => "powerband";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "List Tier×Band target power ranges.";
        public string LongDescription =>
            "With no argument, lists every (Tier, Band) cell (tiers 0-6, bands 1-3) with its target power range. " +
            "With a tier argument, lists just that tier's three band rows.";
        public string Usage => "powerband [tier]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("tier", typeof(string), CommandArgumentKind.Token,
                Required: false, "Tier (0-6) to inspect (omit to list every tier)."),
        });

        public PowerbandCommand(IPowerBudgetSystem powerBudget, PowerBudgetTunables tunables)
        {
            _powerBudget = powerBudget;
            _tunables = tunables;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("tier", out var tierArg);

            if (string.IsNullOrWhiteSpace(tierArg))
            {
                var rows = new List<PowerBandRow>((_tunables.MaxTier + 1) * _tunables.BandsPerTier);
                for (var tier = 0; tier <= _tunables.MaxTier; tier++)
                    rows.AddRange(BuildRows(tier));

                await context.Output.WriteAsync(new PowerbandMessage(rows)).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(tierArg, out var requestedTier) ||
                requestedTier < 0 || requestedTier > _tunables.MaxTier)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Tier must be an integer 0-{_tunables.MaxTier}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PowerbandMessage(BuildRows(requestedTier)))
                .ConfigureAwait(false);
        }

        private List<PowerBandRow> BuildRows(int tier)
        {
            var rows = new List<PowerBandRow>(_tunables.BandsPerTier);
            for (var band = 1; band <= _tunables.BandsPerTier; band++)
                rows.Add(new PowerBandRow(tier, band, _powerBudget.TargetRange(tier, band)));
            return rows;
        }
    }
}
