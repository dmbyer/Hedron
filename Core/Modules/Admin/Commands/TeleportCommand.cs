using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>@teleport &lt;target&gt;</c>. Target is either a room blueprint id
    /// (<c>room.east_end</c>) or a player display name. Updates the invoker's
    /// <see cref="LocationComponent"/> and publishes <see cref="PlayerTeleportedByAdminEvent"/>.
    /// </summary>
    public sealed class TeleportCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IAdminAuthorizer _authorizer;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "@teleport";
        public IReadOnlyList<string> Aliases { get; } = new[] { "@tp" };

        public TeleportCommand(
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

            var target = arguments.Trim();
            if (string.IsNullOrEmpty(target))
            {
                await session.SendLineAsync("Usage: @teleport <roomBlueprintId|playerName>")
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<LocationComponent>(session.PlayerEntityId, out var location))
            {
                await session.SendLineAsync("You have no location.").ConfigureAwait(false);
                return;
            }

            var fromRoomId = location.RoomEntityId;
            var resolvedRoomId = ResolveRoomEntityId(target);
            if (resolvedRoomId is null)
            {
                await session.SendLineAsync($"Cannot resolve teleport target: {target}")
                    .ConfigureAwait(false);
                return;
            }

            location.RoomEntityId = resolvedRoomId.Value;

            await session.SendLineAsync($"Teleported to entity #{resolvedRoomId.Value}.")
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerTeleportedByAdminEvent(
                session.PlayerEntityId,
                session.PlayerEntityId,
                fromRoomId,
                resolvedRoomId.Value)).ConfigureAwait(false);
        }

        private uint? ResolveRoomEntityId(string target)
        {
            if (_templateRegistry.TryGet(target, out _))
            {
                foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
                {
                    if (string.Equals(blueprint.BlueprintId, target, StringComparison.OrdinalIgnoreCase)
                        && _entityService.HasComponent<RoomComponent>(entityId))
                        return entityId;
                }
            }

            foreach (var (entityId, player) in _entityService.GetAllComponents<PlayerComponent>())
            {
                if (string.Equals(player.DisplayName, target, StringComparison.OrdinalIgnoreCase)
                    && _entityService.TryGet<LocationComponent>(entityId, out var theirLoc))
                    return theirLoc.RoomEntityId;
            }

            return null;
        }
    }
}
