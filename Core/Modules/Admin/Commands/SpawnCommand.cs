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
    /// Admin verb <c>@spawn &lt;blueprintId&gt;</c>. Resolves the blueprint via
    /// <see cref="ITemplateRegistry"/>, spawns a fresh entity, and publishes
    /// <see cref="EntitySpawnedByAdminEvent"/>.
    /// </summary>
    public sealed class SpawnCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IAdminAuthorizer _authorizer;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "@spawn";
        public IReadOnlyList<string> Aliases { get; } = System.Array.Empty<string>();

        public SpawnCommand(
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

            var blueprintId = arguments.Trim();
            if (string.IsNullOrEmpty(blueprintId))
            {
                await session.SendLineAsync("Usage: @spawn <blueprintId>").ConfigureAwait(false);
                return;
            }

            if (!_templateRegistry.TryGet(blueprintId, out _))
            {
                await session.SendLineAsync($"Unknown blueprint: {blueprintId}")
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<LocationComponent>(session.PlayerEntityId, out var location))
            {
                await session.SendLineAsync("You have no location — cannot spawn here.")
                    .ConfigureAwait(false);
                return;
            }

            var spawned = _templateRegistry.Spawn(blueprintId);

            // NOTE: Placement-into-the-room is deferred. The only spawnable templates in this
            // slice are rooms and areas, neither of which warrant placement in the invoker's
            // current room. Item and mob templates land with their slices (4 and 6) — at that
            // point this command attaches a LocationComponent (or container reference) to the
            // newly spawned entity. Until then, spawned rooms/areas are orphan entities; use
            // @dig to wire a new room into the live world.

            await session.SendLineAsync($"Spawned {blueprintId} (entity #{spawned.Id}).")
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new EntitySpawnedByAdminEvent(
                session.PlayerEntityId,
                spawned.Id,
                blueprintId,
                location.RoomEntityId)).ConfigureAwait(false);
        }
    }
}
