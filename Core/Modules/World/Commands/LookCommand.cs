using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Commands
{
    public class LookCommand : ICommand
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public string Name => "look";
        public IReadOnlyList<string> Aliases { get; } = new[] { "l" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Look at the current room.";
        public string LongDescription => "Displays a description of your current location, including visible exits and other players present.";
        public string Usage => "look";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public LookCommand(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You are floating in the void.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            await _broadcast.SendRoomDescriptionAsync(context.InvokerEntityId, location.RoomEntityId)
                .ConfigureAwait(false);
        }
    }
}
