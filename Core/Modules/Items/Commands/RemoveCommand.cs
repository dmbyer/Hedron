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
    /// Player verb <c>remove &lt;item&gt;</c>.
    /// Moves a worn item from the invoker's equipment slots back into their inventory.
    /// </summary>
    public sealed class RemoveCommand : ICommand
    {
        private readonly IEquipmentSystem _equipmentSystem;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "remove";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Remove a worn item.";
        public string LongDescription => "Takes off a worn or wielded item and returns it to your inventory.";
        public string Usage => "remove <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; }

        public RemoveCommand(
            IEquipmentSystem equipmentSystem,
            IEventBus eventBus,
            IPersistenceSystem persistence,
            ItemInEquipmentResolver resolver)
        {
            _equipmentSystem = equipmentSystem;
            _eventBus = eventBus;
            _persistence = persistence;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("item", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or keyword of the worn item to remove.", resolver),
            });
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!context.Args.TryGet<string>("item", out var canonicalName) || canonicalName.Length == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("Remove what?", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_equipmentSystem.TryFindEquippedItem(context.InvokerEntityId, canonicalName, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You aren't wearing that.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            // Capture slot list before RemoveItem clears EquipmentComponent.Slots; needed for event payload.
            var slots = _equipmentSystem.GetWornSlots(itemEntityId);
            _equipmentSystem.RemoveItem(context.InvokerEntityId, itemEntityId);

            await _eventBus.PublishAsync(new ItemUnequippedEvent(
                context.InvokerEntityId, itemEntityId, slots))
                .ConfigureAwait(false);

            await _persistence.SaveEntityAsync(context.InvokerEntityId).ConfigureAwait(false);
        }
    }
}
