using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Economy.Commands
{
    /// <summary>
    /// Admin command: absolute-sets a connected player's wallet balance for a given currency.
    ///
    /// <para>
    /// Pattern mirrors <c>SetPlayerCommand</c> exactly:
    /// <list type="bullet">
    ///   <item><description>Admin category + <see cref="AdminRequirement"/> in <see cref="RequiredPrivileges"/>.</description></item>
    ///   <item><description>Session sweep via <see cref="ISessionManager.GetAll"/> to resolve connected player by character name.</description></item>
    ///   <item><description>Absolute-set via <see cref="IWalletSystem.SetBalance"/>, exactly one <see cref="IPersistenceSystem.SaveEntityAsync"/> (admin boundary save, INV-22).</description></item>
    ///   <item><description>Publishes <see cref="WalletSetByAdminEvent"/> for <c>AdminAuditHandler</c>.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class SetwalletCommand : ICommand
    {
        private readonly IWalletSystem _walletSystem;
        private readonly ISessionManager _sessionManager;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "setwallet";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public bool UsableWhileIncapacitated => false;
        public string ShortDescription => "Absolute-set a connected player's wallet balance for a given currency.";
        public string LongDescription =>
            "Sets the balance of a currency in a currently-connected player's wallet to the given " +
            "base-unit amount. The amount is absolute (not additive). " +
            "Valid currencies: Coin. Amount must be a non-negative integer in base units (copper for Coin).";
        public string Usage => "setwallet <characterName> <currency> <amount>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("characterName", typeof(string), CommandArgumentKind.Token,
                Required: true, "Name of the connected character."),
            new CommandArgument("currency", typeof(string), CommandArgumentKind.Token,
                Required: true, "Currency id (e.g. Coin)."),
            new CommandArgument("amount", typeof(string), CommandArgumentKind.Token,
                Required: true, "New balance in base units (non-negative integer)."),
        });

        public SetwalletCommand(
            IWalletSystem walletSystem,
            ISessionManager sessionManager,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _walletSystem = walletSystem;
            _sessionManager = sessionManager;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var characterName = context.Args.Get<string>("characterName");
            var rawCurrency = context.Args.Get<string>("currency");
            var rawAmount = context.Args.Get<string>("amount");

            // Parse currency
            if (!Enum.TryParse<CurrencyId>(rawCurrency, ignoreCase: true, out var currency))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Unknown currency '{rawCurrency}'. Valid currencies: {string.Join(", ", Enum.GetNames<CurrencyId>())}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // Parse amount — must be non-negative
            if (!long.TryParse(rawAmount, out var amount) || amount < 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "Amount must be a non-negative integer.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // Resolve connected player by character name (session sweep, as SetPlayerCommand does)
            uint playerEntityId = 0;
            foreach (var session in _sessionManager.GetAll())
            {
                if (session.PlayerEntityId == 0)
                    continue;
                if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                    string.Equals(ch.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                {
                    playerEntityId = session.PlayerEntityId;
                    break;
                }
            }

            if (playerEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No connected player named '{characterName}'.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // Absolute-set via IWalletSystem
            _walletSystem.SetBalance(playerEntityId, currency, amount);

            // Admin boundary save — exactly one SaveEntityAsync (INV-22)
            await _persistence.SaveEntityAsync(playerEntityId).ConfigureAwait(false);

            // Publish audit event
            await _eventBus.PublishAsync(new WalletSetByAdminEvent(
                context.InvokerEntityId,
                playerEntityId,
                currency,
                amount)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Set {characterName}'s {currency} balance to {amount} copper.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
