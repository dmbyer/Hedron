using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Death.Commands
{
    /// <summary>
    /// Admin command: <c>setrespawn &lt;playerName&gt; &lt;roomBlueprintId&gt;</c>
    /// Sets the named player's respawn room blueprint id. Validates the blueprint exists via
    /// <see cref="IDeathSystem.SetRespawn"/>, persists the change immediately (INV-22 admin
    /// boundary save), then publishes <see cref="PlayerRespawnSetByAdminEvent"/> for the audit log.
    /// </summary>
    public sealed class SetRespawnCommand : ICommand
    {
        private readonly IDeathSystem _deathSystem;
        private readonly ISessionManager _sessionManager;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "setrespawn";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public bool UsableWhileIncapacitated => false;
        public string ShortDescription => "Set a player's respawn room.";
        public string LongDescription =>
            "Sets the room blueprint id that the named player will respawn into after death. " +
            "The blueprint must exist in the template registry. " +
            "The change is persisted immediately (admin boundary save, INV-22) and audited.";
        public string Usage => "setrespawn <characterName> <roomBlueprintId>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("characterName", typeof(string), CommandArgumentKind.Token,
                Required: true, "Name of the connected character."),
            new CommandArgument("roomBlueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the respawn room."),
        });

        public SetRespawnCommand(
            IDeathSystem deathSystem,
            ISessionManager sessionManager,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _deathSystem = deathSystem ?? throw new ArgumentNullException(nameof(deathSystem));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var characterName = context.Args.Get<string>("characterName");
            var roomBlueprintId = context.Args.Get<string>("roomBlueprintId");

            // Resolve the target player entity by character name across all connected sessions.
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
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            // Delegate validation + mutation to IDeathSystem (blueprint-existence check happens there).
            if (!_deathSystem.SetRespawn(playerEntityId, roomBlueprintId, out var failReason))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    failReason ?? "Failed to set respawn room.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            // Admin boundary save (INV-22) — persist the respawn room durably right away.
            await _persistence.SaveEntityAsync(playerEntityId).ConfigureAwait(false);

            // Audit event — commands are initiators and are responsible for publishing (INV-5).
            await _eventBus.PublishAsync(new PlayerRespawnSetByAdminEvent(
                context.InvokerEntityId,
                playerEntityId,
                roomBlueprintId)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Respawn room for {characterName} set to '{roomBlueprintId}'.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
