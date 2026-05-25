using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Resolvers;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Player verb <c>get &lt;item&gt;</c>.
    /// Picks up a named item from the invoker's current room into their inventory.
    /// </summary>
    public sealed class GetCommand : ICommand
    {
        private readonly IItemSystem _itemSystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "get";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Pick up an item from the room.";
        public string LongDescription => "Picks up a named item from the ground and adds it to your inventory.";
        public string Usage => "get <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; }

        public GetCommand(
            IItemSystem itemSystem,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence,
            ItemInRoomResolver resolver)
        {
            _itemSystem = itemSystem;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("item", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or keyword of the item to pick up.", resolver),
            });
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            if (!context.Args.TryGet<string>("item", out var canonicalName) || canonicalName.Length == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("Get what?", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_itemSystem.TryFindItemInRoom(location.RoomEntityId, canonicalName, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You don't see that here.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var roomEntityId = location.RoomEntityId;
            _itemSystem.MoveToInventory(itemEntityId, context.InvokerEntityId);

            await _eventBus.PublishAsync(new ItemPickedUpEvent(
                context.InvokerEntityId, itemEntityId, roomEntityId))
                .ConfigureAwait(false);

            await _persistence.SaveEntityAsync(itemEntityId).ConfigureAwait(false);
            await _persistence.SaveEntityAsync(context.InvokerEntityId).ConfigureAwait(false);
        }
    }
}
