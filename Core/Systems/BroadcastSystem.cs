using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Sessions;

namespace Hedron.Core.Systems
{
    public class BroadcastSystem : IBroadcastSystem
    {
        private readonly EntityService _entityService;
        private readonly ISessionManager _sessionManager;

        public BroadcastSystem(EntityService entityService, ISessionManager sessionManager)
        {
            _entityService = entityService;
            _sessionManager = sessionManager;
        }

        public async Task SendToPlayerAsync(uint playerEntityId, string message)
        {
            var session = _sessionManager.GetSession(playerEntityId);
            if (session != null)
                await session.SendLineAsync(message).ConfigureAwait(false);
        }

        public async Task SendToRoomAsync(uint roomEntityId, string message, uint? excludeEntityId = null)
        {
            foreach (var (entityId, location) in _entityService.GetAllComponents<LocationComponent>())
            {
                if (location.RoomEntityId != roomEntityId) continue;
                if (excludeEntityId.HasValue && entityId == excludeEntityId.Value) continue;
                if (!_entityService.HasComponent<PlayerComponent>(entityId)) continue;

                await SendToPlayerAsync(entityId, message).ConfigureAwait(false);
            }
        }

        public async Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId)
        {
            if (!_entityService.TryGet<RoomComponent>(roomEntityId, out var room))
                return;

            var exits = room.Exits.Count > 0
                ? string.Join(", ", room.Exits.Keys.Select(d => d.ToString().ToLower()))
                : "none";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(room.Name);
            sb.AppendLine(room.Description);
            sb.Append($"Exits: {exits}");

            foreach (var (entityId, loc) in _entityService.GetAllComponents<LocationComponent>())
            {
                if (loc.RoomEntityId != roomEntityId || entityId == playerEntityId) continue;
                if (_entityService.TryGet<PlayerComponent>(entityId, out var occupant))
                    sb.AppendLine().Append($"{occupant.DisplayName} is here.");
            }

            await SendToPlayerAsync(playerEntityId, sb.ToString()).ConfigureAwait(false);
        }
    }
}
