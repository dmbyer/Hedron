using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Admin verb <c>setitem &lt;blueprintId&gt; &lt;property&gt; &lt;value&gt;</c>.
    /// Mutates name, description, keywords, or type on a live item entity identified by blueprint id.
    /// </summary>
    public sealed class SetitemCommand : ICommand
    {
        private readonly IItemBuilderSystem _itemBuilder;
        private readonly IItemContentWriter _contentWriter;
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IEventBus _eventBus;

        public string Name => "setitem";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on an item.";
        public string LongDescription =>
            "Sets name, description, keywords (space-separated), type, slot, or dmg on the item with the given blueprint id. " +
            "Valid types: none, weapon, armor, consumable, container, misc. " +
            "Valid slots (space-separated): mainhand, offhand, head, chest, feet. " +
            "dmg accepts a non-negative integer (flat damage bonus applied when equipped in MainHand).";
        public string Usage => "setitem <blueprintId> <property> <value>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the target item."),
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: name, description, keywords, type."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "New value."),
        });

        public SetitemCommand(
            IItemBuilderSystem itemBuilder,
            IItemContentWriter contentWriter,
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IEventBus eventBus)
        {
            _itemBuilder = itemBuilder;
            _contentWriter = contentWriter;
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var blueprintId = context.Args.Get<string>("blueprintId");
            var property = context.Args.Get<string>("property").ToLowerInvariant();
            var value = context.Args.Get<string>("value");

            if (!_templateRegistry.TryGet(blueprintId, out _))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No item template found with blueprint id '{blueprintId}'.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            uint itemEntityId = 0;
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase) &&
                    _entityService.HasComponent<ItemDataComponent>(entityId))
                {
                    itemEntityId = entityId;
                    break;
                }
            }

            if (itemEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Item '{blueprintId}' has no live entity in the world.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            switch (property)
            {
                case "name":
                    _itemBuilder.SetItemName(itemEntityId, value);
                    break;

                case "description":
                    _itemBuilder.SetItemDescription(itemEntityId, value);
                    break;

                case "keywords":
                    var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    _itemBuilder.SetItemKeywords(itemEntityId, keywords);
                    break;

                case "type":
                    if (!Enum.TryParse<ItemType>(value, ignoreCase: true, out var itemType))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown item type '{value}'. Valid types: none, weapon, armor, consumable, container, misc.",
                            OutputSeverity.Error)).ConfigureAwait(false);
                        return;
                    }
                    _itemBuilder.SetItemType(itemEntityId, itemType);
                    break;

                case "slot":
                {
                    var slotNames = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var parsedSlots = new List<WornSlot>(slotNames.Length);
                    foreach (var slotName in slotNames)
                    {
                        if (!Enum.TryParse<WornSlot>(slotName, ignoreCase: true, out var wornSlot))
                        {
                            await context.Output.WriteAsync(new PlainMessage(
                                $"Unknown slot '{slotName}'. Valid slots: mainhand, offhand, head, chest, feet.",
                                OutputSeverity.Error)).ConfigureAwait(false);
                            return;
                        }
                        parsedSlots.Add(wornSlot);
                    }
                    _itemBuilder.SetItemSlots(itemEntityId, parsedSlots);
                    break;
                }

                case "dmg":
                {
                    if (!int.TryParse(value, out var dmgValue))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Invalid damage bonus '{value}'. Expected a non-negative integer.",
                            OutputSeverity.Error)).ConfigureAwait(false);
                        return;
                    }
                    if (dmgValue < 0)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Damage bonus must be non-negative.",
                            OutputSeverity.Error)).ConfigureAwait(false);
                        return;
                    }
                    _itemBuilder.SetItemDamageBonus(itemEntityId, dmgValue);
                    break;
                }

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, slot, dmg.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;
            }

            await _eventBus.PublishAsync(new ItemPropertySetByAdminEvent(
                context.InvokerEntityId,
                itemEntityId,
                property,
                value)).ConfigureAwait(false);

            if (_templateRegistry.TryGet(blueprintId, out var tpl) &&
                tpl is Hedron.Core.Modules.Items.Templates.ItemTemplate itemTpl)
                await _contentWriter.WriteAsync(itemTpl).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Item {property} set to '{value}'.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
