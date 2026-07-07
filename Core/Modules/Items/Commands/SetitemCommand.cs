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
using Hedron.Core.Modules.Stats;
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
            "Sets name, description, keywords (space-separated), type, slot, value, or worn-stat bonuses on the item with the given blueprint id. " +
            "Valid types: none, weapon, armor, consumable, container, misc. " +
            "Valid slots (space-separated): mainhand, offhand, head, chest, feet, legs, hands, arms, waist, neck, finger, finger2, wrist, wrist2. " +
            "value <n> sets the item's intrinsic base-unit coin value (non-negative integer; 0 = valueless/non-saleable). " +
            "bonus <score> <amount> adds or replaces a worn stat bonus (amount 0 removes that score; negative is allowed for cursed gear); " +
            "clearbonus removes all bonuses. Valid scores: attackpower, defense (any score id). " +
            "band (Ascension tier-band tag, integer 0-6, 0 = unbanded).";
        public string Usage => "setitem <blueprintId> <property> [value]  (properties: name, description, keywords, type, slot, value, bonus, clearbonus, band)";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the target item."),
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: name, description, keywords, type, slot, bonus, clearbonus."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "New value (omit only for clearbonus)."),
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
            context.Args.TryGet<string>("value", out var valueArg);
            var value = valueArg ?? string.Empty;

            if (!_templateRegistry.TryGet(blueprintId, out _))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No item template found with blueprint id '{blueprintId}'.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
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
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // Every property except clearbonus needs a value; reject early so the cases can assume one.
            if (property != "clearbonus" && string.IsNullOrWhiteSpace(value))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Property '{property}' requires a value.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
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

                case "value":
                    if (!long.TryParse(value, out var itemValue) || itemValue < 0)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Invalid value '{value}'. Expected a non-negative integer (e.g. 250).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    _itemBuilder.SetItemValue(itemEntityId, itemValue);
                    break;

                case "type":
                    if (!Enum.TryParse<ItemType>(value, ignoreCase: true, out var itemType))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown item type '{value}'. Valid types: none, weapon, armor, consumable, container, misc.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
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
                                $"Unknown slot '{slotName}'. Valid slots: mainhand, offhand, head, chest, feet, legs, hands, arms, waist, neck, finger, finger2, wrist, wrist2.",
                                OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                            return;
                        }
                        parsedSlots.Add(wornSlot);
                    }
                    _itemBuilder.SetItemSlots(itemEntityId, parsedSlots);
                    break;
                }

                case "bonus":
                {
                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Usage: setitem <blueprintId> bonus <score> <amount>.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    if (!Enum.TryParse<ScoreId>(parts[0], ignoreCase: true, out var score))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown score '{parts[0]}'. Valid scores include attackpower, defense.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    if (!int.TryParse(parts[1], out var magnitude))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Invalid amount '{parts[1]}'. Expected an integer (0 removes the bonus).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    _itemBuilder.SetItemStatBonus(itemEntityId, score, magnitude);
                    break;
                }

                case "clearbonus":
                    _itemBuilder.ClearItemStatBonuses(itemEntityId);
                    break;

                case "band":
                    if (!int.TryParse(value, out var tierBand) || tierBand < 0 || tierBand > 6)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Tier band must be an integer 0-6 (0 = unbanded).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    _itemBuilder.SetItemBand(itemEntityId, tierBand);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, slot, value, bonus, clearbonus, band.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
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

            var confirmation = property == "clearbonus"
                ? "Item bonuses cleared."
                : $"Item {property} set to '{value}'.";
            await context.Output.WriteAsync(new PlainMessage(
                confirmation,
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
