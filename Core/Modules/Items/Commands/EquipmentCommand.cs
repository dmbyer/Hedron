using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Player verb <c>equipment</c> (aliases: <c>eq</c>).
    /// Displays all worn items by slot. No events.
    /// </summary>
    public sealed class EquipmentCommand : ICommand
    {
        private readonly EntityService _entityService;

        public string Name => "equipment";
        public IReadOnlyList<string> Aliases { get; } = new[] { "eq" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Show your worn equipment.";
        public string LongDescription => "Lists all items you are currently wearing or wielding, by slot.";
        public string Usage => "equipment";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(Array.Empty<CommandArgument>());

        public EquipmentCommand(EntityService entityService)
        {
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityService.TryGet<EquipmentComponent>(context.InvokerEntityId, out var eq) ||
                eq.Slots.Count == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You are not wearing anything.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            var rows = new List<(string SlotLabel, string ItemName)>();
            foreach (WornSlot slot in Enum.GetValues<WornSlot>())
            {
                if (!eq.Slots.TryGetValue(slot, out var itemEntityId)) continue;
                var itemName = _entityService.TryGet<ItemDataComponent>(itemEntityId, out var data)
                    ? data.Name
                    : "something";
                rows.Add(($"[{FormatSlot(slot)}]", itemName));
            }

            await context.Output.WriteAsync(new EquipmentDisplayMessage(rows))
                .ConfigureAwait(false);
        }

        private static string FormatSlot(WornSlot slot) => slot switch
        {
            WornSlot.MainHand => "Main Hand",
            WornSlot.OffHand  => "Off Hand",
            WornSlot.Head     => "Head",
            WornSlot.Chest    => "Chest",
            WornSlot.Feet     => "Feet",
            _                 => slot.ToString(),
        };
    }
}
