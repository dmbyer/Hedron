using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Output;
using Hedron.Core.Sessions;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Delivers typed output to rooms, all sessions, or a single player by composing
    /// <see cref="IOutputWriterFactory"/> per recipient so every message goes through the
    /// transport-correct formatter pipeline.
    /// </summary>
    public class BroadcastSystem : IBroadcastSystem
    {
        private readonly EntityService _entityService;
        private readonly ISessionManager _sessionManager;
        private readonly IOutputWriterFactory _writerFactory;

        public BroadcastSystem(
            EntityService entityService,
            ISessionManager sessionManager,
            IOutputWriterFactory writerFactory)
        {
            _entityService = entityService;
            _sessionManager = sessionManager;
            _writerFactory = writerFactory;
        }

        public async Task SendToRoomAsync(
            uint roomEntityId,
            IOutputMessage message,
            Func<uint, bool>? audienceFilter = null)
        {
            foreach (var (entityId, location) in _entityService.GetAllComponents<LocationComponent>())
            {
                if (location.RoomEntityId != roomEntityId) continue;
                if (!_entityService.HasComponent<PlayerComponent>(entityId)) continue;
                if (audienceFilter != null && !audienceFilter(entityId)) continue;

                var session = _sessionManager.GetSession(entityId);
                if (session == null) continue;

                await _writerFactory.Create(session).WriteAsync(message).ConfigureAwait(false);
            }
        }

        public async Task SendToAllAsync(IOutputMessage message)
        {
            foreach (var session in _sessionManager.GetAll())
                await _writerFactory.Create(session).WriteAsync(message).ConfigureAwait(false);
        }

        public async Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId)
        {
            if (!_entityService.TryGet<RoomComponent>(roomEntityId, out var room))
                return;

            var session = _sessionManager.GetSession(playerEntityId);
            if (session == null) return;

            var exits = new Dictionary<Direction, string>();
            foreach (var kvp in room.Exits)
                exits[kvp.Key] = kvp.Key.ToString().ToLower();

            var occupants = new List<string>();
            foreach (var (entityId, loc) in _entityService.GetAllComponents<LocationComponent>())
            {
                if (loc.RoomEntityId != roomEntityId || entityId == playerEntityId) continue;
                if (_entityService.TryGet<PlayerComponent>(entityId, out var occupant))
                    occupants.Add(occupant.DisplayName);
            }

            var items = new List<string>();
            foreach (var (entityId, itemData) in _entityService.GetAllComponents<ItemDataComponent>())
            {
                if (_entityService.TryGet<LocationComponent>(entityId, out var itemLoc) &&
                    itemLoc.RoomEntityId == roomEntityId)
                    items.Add(itemData.Name);
            }

            var msg = new RoomDescriptionMessage(
                roomEntityId, room.Name, room.Description, exits, occupants, items);

            await _writerFactory.Create(session).WriteAsync(msg).ConfigureAwait(false);
        }
    }
}
