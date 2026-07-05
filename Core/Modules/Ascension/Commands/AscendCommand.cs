using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Ascension.Events;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Ascension.Commands
{
    /// <summary>
    /// Admin command: <c>ascend [characterName]</c>. Ascends the named connected player one tier
    /// (defaults to the invoker when omitted). Validates eligibility via
    /// <see cref="IAscensionSystem.CanAscend"/>, mutates via <see cref="IAscensionSystem.TryAscend"/>,
    /// persists the change immediately (INV-22 admin boundary save), then publishes
    /// <see cref="AscendedEvent"/> (milestone) and <see cref="PlayerAscendedByAdminEvent"/> (audit).
    /// Mirrors <c>SetRespawnCommand</c>. The real player-facing Ascension-Objective gate is
    /// deferred — this command is the interim trigger.
    /// </summary>
    public sealed class AscendCommand : ICommand
    {
        private readonly IAscensionSystem _ascensionSystem;
        private readonly ISessionManager _sessionManager;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "ascend";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "Ascend a player one tier.";
        public string LongDescription =>
            "Ascends the named connected player one character-wide tier (defaults to yourself if omitted). " +
            "Rejected if already at max tier. The change is persisted immediately (admin boundary save, INV-22) and audited.";
        public string Usage => "ascend [characterName]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("characterName", typeof(string), CommandArgumentKind.Token,
                Required: false, "Name of the connected character to ascend (omit to ascend yourself)."),
        });

        public AscendCommand(
            IAscensionSystem ascensionSystem,
            ISessionManager sessionManager,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _ascensionSystem = ascensionSystem;
            _sessionManager = sessionManager;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("characterName", out var characterName);

            uint targetEntityId;
            if (string.IsNullOrWhiteSpace(characterName))
            {
                targetEntityId = context.InvokerEntityId;
            }
            else
            {
                targetEntityId = 0;
                foreach (var session in _sessionManager.GetAll())
                {
                    if (session.PlayerEntityId == 0)
                        continue;
                    if (_entityService.TryGet<CharacterComponent>(session.PlayerEntityId, out var ch) &&
                        string.Equals(ch.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetEntityId = session.PlayerEntityId;
                        break;
                    }
                }

                if (targetEntityId == 0)
                {
                    await context.Output.WriteAsync(new PlainMessage(
                        $"No connected player named '{characterName}'.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
                }
            }

            var eligibility = _ascensionSystem.CanAscend(targetEntityId);
            if (!eligibility.Eligible)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Cannot ascend: {eligibility.Reason}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            var result = _ascensionSystem.TryAscend(targetEntityId);
            if (!result.Success)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Cannot ascend: {result.FailureReason}.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // Admin boundary save (INV-22) — persist the new tier and unlock-record state durably.
            await _persistence.SaveEntityAsync(targetEntityId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new AscendedEvent(
                targetEntityId, result.NewTier, result.PreviousTier)).ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerAscendedByAdminEvent(
                context.InvokerEntityId, targetEntityId, result.NewTier)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Ascended to Tier {result.NewTier}.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
