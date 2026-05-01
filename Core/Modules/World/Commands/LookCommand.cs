using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Commands
{
    public class LookCommand : ICommand
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public string Name => "look";
        public IReadOnlyList<string> Aliases { get; } = new[] { "l" };

        public LookCommand(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task ExecuteAsync(ISession session, string arguments)
        {
            if (!_entityService.TryGet<LocationComponent>(session.PlayerEntityId, out var location))
            {
                await session.SendLineAsync("You are floating in the void.").ConfigureAwait(false);
                return;
            }

            await _broadcast.SendRoomDescriptionAsync(session.PlayerEntityId, location.RoomEntityId)
                .ConfigureAwait(false);
        }
    }
}
