using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Commands
{
    public class LookCommand : ICommand
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;
        private readonly IItemSystem _itemSystem;

        public string Name => "look";
        public IReadOnlyList<string> Aliases { get; } = new[] { "l" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Look at the current room or a specific item.";
        public string LongDescription => "Displays a description of your current location, including visible exits and other players present. Supply a name to examine a specific item in the room.";
        public string Usage => "look [target]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name or keyword of an item to examine."),
        });

        public LookCommand(EntityService entityService, IBroadcastSystem broadcast, IItemSystem itemSystem)
        {
            _entityService = entityService;
            _broadcast = broadcast;
            _itemSystem = itemSystem;
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

            var hasTarget = context.Args.TryGet<string>("target", out var target) &&
                            target.Length > 0;

            if (!hasTarget)
            {
                await _broadcast.SendRoomDescriptionAsync(context.InvokerEntityId, location.RoomEntityId)
                    .ConfigureAwait(false);
                return;
            }

            // Room first, then inventory fallback.
            var found = _itemSystem.TryFindItemInRoom(location.RoomEntityId, target, out var itemEntityId)
                     || _itemSystem.TryFindItemInInventory(context.InvokerEntityId, target, out itemEntityId);

            if (found && _entityService.TryGet<ItemDataComponent>(itemEntityId, out var itemData))
            {
                await context.Output.WriteAsync(
                    new PlainMessage($"{itemData.Name}\n{itemData.Description}", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(
                new PlainMessage("You don't see that here.", OutputSeverity.System))
                .ConfigureAwait(false);
        }
    }
}
