using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Resolvers;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Player verb <c>wear &lt;item&gt;</c>.
    /// Moves a named item from the invoker's inventory into their equipment slots.
    /// </summary>
    public sealed class WearCommand : ICommand
    {
        private readonly IItemSystem _itemSystem;
        private readonly IEquipmentSystem _equipmentSystem;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "wear";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Wear an item from your inventory.";
        public string LongDescription => "Puts on a wearable or wieldable item from your inventory, placing it in the appropriate equipment slot.";
        public string Usage => "wear <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; }

        public WearCommand(
            IItemSystem itemSystem,
            IEquipmentSystem equipmentSystem,
            IEventBus eventBus,
            IPersistenceSystem persistence,
            ItemInInventoryResolver resolver)
        {
            _itemSystem = itemSystem;
            _equipmentSystem = equipmentSystem;
            _eventBus = eventBus;
            _persistence = persistence;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("item", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or keyword of the item to wear.", resolver),
            });
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!context.Args.TryGet<string>("item", out var canonicalName) || canonicalName.Length == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("Wear what?", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_itemSystem.TryFindItemInInventory(context.InvokerEntityId, canonicalName, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You aren't carrying that.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var slots = _equipmentSystem.GetWornSlots(itemEntityId);
            if (slots.Count == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You can't wear that.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            _equipmentSystem.EquipItem(context.InvokerEntityId, itemEntityId);

            await _eventBus.PublishAsync(new ItemEquippedEvent(
                context.InvokerEntityId, itemEntityId, slots))
                .ConfigureAwait(false);

            await _persistence.SaveEntityAsync(context.InvokerEntityId).ConfigureAwait(false);
        }
    }
}
