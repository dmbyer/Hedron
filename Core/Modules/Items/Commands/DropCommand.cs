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
    /// Player verb <c>drop &lt;item&gt;</c>.
    /// Drops a named item from the invoker's inventory to their current room.
    /// Intentionally does NOT save the item entity after drop — dropped items vanish on restart
    /// (design decision documented in the items-and-inventory use-case spec).
    /// </summary>
    public sealed class DropCommand : ICommand
    {
        private readonly IItemSystem _itemSystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "drop";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Drop an item from your inventory.";
        public string LongDescription => "Drops a named item from your inventory onto the ground in the current room.";
        public string Usage => "drop <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; }

        public DropCommand(
            IItemSystem itemSystem,
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence,
            ItemInInventoryResolver resolver)
        {
            _itemSystem = itemSystem;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("item", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or keyword of the item to drop.", resolver),
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
                    new PlainMessage("Drop what?", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_itemSystem.TryFindItemInInventory(context.InvokerEntityId, canonicalName, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You aren't carrying that.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var roomEntityId = location.RoomEntityId;
            _itemSystem.DropToRoom(itemEntityId, context.InvokerEntityId, roomEntityId);

            await _eventBus.PublishAsync(new ItemDroppedEvent(
                context.InvokerEntityId, itemEntityId, roomEntityId))
                .ConfigureAwait(false);

            // Save player only — item is intentionally not saved after drop so dropped items
            // vanish on restart (see items-and-inventory use-case spec, Design Notes).
            await _persistence.SaveEntityAsync(context.InvokerEntityId).ConfigureAwait(false);
        }
    }
}
