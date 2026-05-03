using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>@dig &lt;direction&gt; &lt;targetRoomBlueprintId&gt;</c>.
    /// Adds an exit from the invoker's current room to the target room, wires the reverse
    /// exit on the target by default, and updates the in-memory <see cref="RoomTemplate"/>
    /// so a same-session <c>@reload</c> won't undo the change.
    /// </summary>
    /// <remarks>
    /// The source YAML file on disk is <i>not</i> rewritten. Durability of the room shape
    /// comes from <c>PersistenceSystem</c> saving the live room's <c>[Persistent]</c>
    /// components on its next timed flush.
    /// </remarks>
    public sealed class DigCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IAdminAuthorizer _authorizer;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "@dig";
        public IReadOnlyList<string> Aliases { get; } = System.Array.Empty<string>();

        public DigCommand(
            ITemplateRegistry templateRegistry,
            IAdminAuthorizer authorizer,
            EntityService entityService,
            IEventBus eventBus)
        {
            _templateRegistry = templateRegistry;
            _authorizer = authorizer;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(ISession session, string arguments)
        {
            if (!_authorizer.IsPrivileged(session))
            {
                await session.SendLineAsync("You are not authorized to use that command.")
                    .ConfigureAwait(false);
                return;
            }

            var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await session.SendLineAsync("Usage: @dig <direction> <targetRoomBlueprintId>")
                    .ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse<Direction>(parts[0], ignoreCase: true, out var direction))
            {
                await session.SendLineAsync($"Unknown direction: {parts[0]}").ConfigureAwait(false);
                return;
            }

            var targetBlueprintId = parts[1];
            if (!_entityService.TryGet<LocationComponent>(session.PlayerEntityId, out var location))
            {
                await session.SendLineAsync("You have no location.").ConfigureAwait(false);
                return;
            }

            var sourceRoomId = location.RoomEntityId;
            if (!_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
            {
                await session.SendLineAsync("Your current location is not a room.")
                    .ConfigureAwait(false);
                return;
            }

            var targetRoomId = ResolveRoomEntityId(targetBlueprintId);
            if (targetRoomId is null)
            {
                await session.SendLineAsync($"Cannot resolve target room: {targetBlueprintId}")
                    .ConfigureAwait(false);
                return;
            }

            sourceRoom.Exits[direction] = targetRoomId.Value;

            // Update the in-memory template if we know the source room's blueprint id.
            UpdateTemplate(sourceRoomId, direction, targetBlueprintId);

            // Bidirectional reverse link by default.
            var bidirectional = false;
            var opposite = Opposite(direction);
            if (opposite is not null
                && _entityService.TryGet<RoomComponent>(targetRoomId.Value, out var targetRoom)
                && !targetRoom.Exits.ContainsKey(opposite.Value))
            {
                targetRoom.Exits[opposite.Value] = sourceRoomId;
                bidirectional = true;

                if (_entityService.TryGet<BlueprintComponent>(sourceRoomId, out var sourceBlueprint))
                    UpdateTemplate(targetRoomId.Value, opposite.Value, sourceBlueprint.BlueprintId);
            }

            await session.SendLineAsync(
                $"Dug {direction.ToString().ToLower()} → {targetBlueprintId}"
                + (bidirectional ? " (reverse exit linked)." : "."))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new RoomExitAuthoredByAdminEvent(
                session.PlayerEntityId,
                sourceRoomId,
                direction,
                targetRoomId.Value,
                bidirectional)).ConfigureAwait(false);
        }

        private uint? ResolveRoomEntityId(string blueprintId)
        {
            foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(blueprint.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase)
                    && _entityService.HasComponent<RoomComponent>(entityId))
                    return entityId;
            }
            return null;
        }

        private void UpdateTemplate(uint roomEntityId, Direction direction, string targetBlueprintId)
        {
            if (!_entityService.TryGet<BlueprintComponent>(roomEntityId, out var blueprint))
                return;
            if (!_templateRegistry.TryGet(blueprint.BlueprintId, out var template))
                return;
            if (template is RoomTemplate roomTemplate)
                roomTemplate.Exits[direction] = targetBlueprintId;
        }

        private static Direction? Opposite(Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _               => null,
        };
    }
}
