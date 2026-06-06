using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Player verb <c>inventory</c> (aliases: <c>inv</c>, <c>i</c>).
    /// Lists items the invoker is currently carrying.
    /// </summary>
    public sealed class InventoryCommand : ICommand
    {
        private readonly IItemSystem _itemSystem;
        private readonly EntityService _entityService;

        public string Name => "inventory";
        public IReadOnlyList<string> Aliases { get; } = new[] { "inv", "i" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List items you are carrying.";
        public string LongDescription => "Displays a list of everything currently in your inventory.";
        public string Usage => "inventory";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } =
            new CommandArgumentSchema(Array.Empty<CommandArgument>());

        public InventoryCommand(IItemSystem itemSystem, EntityService entityService)
        {
            _itemSystem = itemSystem;
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var itemIds = _itemSystem.GetItemsInInventory(context.InvokerEntityId);

            if (itemIds.Count == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You are carrying nothing.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var names = new List<string>(itemIds.Count);
            foreach (var itemId in itemIds)
            {
                if (_entityService.TryGet<ItemDataComponent>(itemId, out var data))
                    names.Add(data.Name);
            }

            await context.Output.WriteAsync(new InventoryListMessage(names))
                .ConfigureAwait(false);
        }
    }
}
