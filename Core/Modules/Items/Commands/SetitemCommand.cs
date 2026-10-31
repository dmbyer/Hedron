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
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Admin verb <c>setitem &lt;blueprintId&gt; &lt;property&gt; &lt;value&gt;</c>.
    /// Sets a property on an item <b>blueprint</b>: the builder updates the template and every live
    /// world instance (never a <c>PersistentEntity</c>, e.g. a player-owned copy, INV-21); with no
    /// live instances only the template changes. The command writes the YAML and publishes
    /// <see cref="ItemPropertySetByAdminEvent"/> with the instance count.
    /// </summary>
    public sealed class SetitemCommand : ICommand
    {
        private readonly IItemBuilderSystem _itemBuilder;
        private readonly IItemContentWriter _contentWriter;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IEventBus _eventBus;

        public string Name => "setitem";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on an item.";
        public string LongDescription =>
            "Sets name, description, keywords (space-separated), type, slot, value, or worn-stat bonuses on the item blueprint with the given id, updating the template and every live world instance (player-owned copies are untouched). " +
            "Valid types: none, weapon, armor, consumable, container, misc. " +
            "Valid slots (space-separated): mainhand, offhand, head, chest, feet, legs, hands, arms, waist, neck, finger, finger2, wrist, wrist2. " +
            "value <n> sets the item's intrinsic base-unit coin value (non-negative integer; 0 = valueless/non-saleable). " +
            "bonus <score> <amount> adds or replaces a worn stat bonus (amount 0 removes that score; negative is allowed for cursed gear); " +
            "clearbonus removes all bonuses. Valid scores: attackpower, defense (any score id). " +
            "tier (Ascension tier tag, integer 0-6, 0 = unbanded/base). " +
            "band (descriptive Band tag, integer 0-3, 0 = unbanded).";
        public string Usage => "setitem <blueprintId> <property> [value]  (properties: name, description, keywords, type, slot, value, bonus, clearbonus, tier, band)";
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
            ITemplateRegistry templateRegistry,
            IEventBus eventBus)
        {
            _itemBuilder = itemBuilder;
            _contentWriter = contentWriter;
            _templateRegistry = templateRegistry;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var blueprintId = context.Args.Get<string>("blueprintId");
            var property = context.Args.Get<string>("property").ToLowerInvariant();
            context.Args.TryGet<string>("value", out var valueArg);
            var value = valueArg ?? string.Empty;

            if (!_templateRegistry.TryGet(blueprintId, out var tplRaw) || tplRaw is not ItemTemplate template)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No item template found with blueprint id '{blueprintId}'.",
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

            var instances = 0;
            switch (property)
            {
                case "name":
                    instances = _itemBuilder.SetItemName(blueprintId, value);
                    break;

                case "description":
                    instances = _itemBuilder.SetItemDescription(blueprintId, value);
                    break;

                case "keywords":
                    var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    instances = _itemBuilder.SetItemKeywords(blueprintId, keywords);
                    break;

                case "value":
                    if (!long.TryParse(value, out var itemValue) || itemValue < 0)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Invalid value '{value}'. Expected a non-negative integer (e.g. 250).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _itemBuilder.SetItemValue(blueprintId, itemValue);
                    break;

                case "type":
                    if (!Enum.TryParse<ItemType>(value, ignoreCase: true, out var itemType))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown item type '{value}'. Valid types: none, weapon, armor, consumable, container, misc.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _itemBuilder.SetItemType(blueprintId, itemType);
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
                    instances = _itemBuilder.SetItemSlots(blueprintId, parsedSlots);
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
                    instances = _itemBuilder.SetItemStatBonus(blueprintId, score, magnitude);
                    break;
                }

                case "clearbonus":
                    instances = _itemBuilder.ClearItemStatBonuses(blueprintId);
                    break;

                case "tier":
                    if (!int.TryParse(value, out var tier) || tier < 0 || tier > 6)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Tier must be an integer 0-6 (0 = unbanded/base).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _itemBuilder.SetItemTier(blueprintId, tier);
                    break;

                case "band":
                    if (!int.TryParse(value, out var band) || band < 0 || band > 3)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Band must be an integer 0-3 (0 = unbanded).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _itemBuilder.SetItemBand(blueprintId, band);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, slot, value, bonus, clearbonus, tier, band.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
            }

            await _eventBus.PublishAsync(new ItemPropertySetByAdminEvent(
                context.InvokerEntityId,
                template.BlueprintId,
                property,
                value,
                instances)).ConfigureAwait(false);

            await _contentWriter.WriteAsync(template).ConfigureAwait(false);

            var confirmation = property == "clearbonus"
                ? $"Item bonuses cleared ({instances} live instance(s) updated)."
                : $"Item {property} set to '{value}' ({instances} live instance(s) updated).";
            await context.Output.WriteAsync(new PlainMessage(
                confirmation,
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
