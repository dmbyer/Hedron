using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Commands
{
    public sealed class SetMobCommand : ICommand
    {
        private readonly IMobBuilderSystem _mobBuilder;
        private readonly IMobContentWriter _contentWriter;
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IEventBus _eventBus;

        public string Name => "setmob";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on a mob.";
        public string LongDescription =>
            "Sets a property on the mob with the given blueprint id. " +
            "Valid properties: name, description, keywords (space-separated), type (none/vendor/guard/creature), " +
            "level, hp, mind, body, spirit, attunement, maxmana, maxstamina, maxastra, " +
            "protection (comma or space-separated flags: none, untargetable, effectimmune), " +
            "shop (\"off\" to clear, or \"on [tillSeed] [currency]\" to make a shopkeeper; " +
            "base-stock rows are authored via the content editor / YAML).";
        public string Usage => "setmob <blueprintId> <property> <value>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the target mob."),
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: name, description, keywords, type."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "New value."),
        });

        public SetMobCommand(
            IMobBuilderSystem mobBuilder,
            IMobContentWriter contentWriter,
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IEventBus eventBus)
        {
            _mobBuilder = mobBuilder;
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

            if (!_templateRegistry.TryGet(blueprintId, out var tplRaw) || tplRaw is not MobTemplate template)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"No mob template found with blueprint id '{blueprintId}'.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            uint mobEntityId = 0;
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bp.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase) &&
                    _entityService.HasComponent<MobDataComponent>(entityId))
                {
                    mobEntityId = entityId;
                    break;
                }
            }

            if (mobEntityId == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Mob '{blueprintId}' has no live entity in the world.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            switch (property)
            {
                case "name":
                    _mobBuilder.SetMobName(mobEntityId, value);
                    break;

                case "description":
                    _mobBuilder.SetMobDescription(mobEntityId, value);
                    break;

                case "keywords":
                    var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    _mobBuilder.SetMobKeywords(mobEntityId, keywords);
                    break;

                case "type":
                    if (!Enum.TryParse<MobType>(value, ignoreCase: true, out var mobType))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown mob type '{value}'. Valid types: none, vendor, guard, creature.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    _mobBuilder.SetMobType(mobEntityId, mobType);
                    break;

                case "level":
                case "hp":
                case "mind":
                case "body":
                case "spirit":
                case "attunement":
                case "maxmana":
                case "maxstamina":
                case "maxastra":
                    if (!int.TryParse(value, out var numericValue) || numericValue < 1)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Value must be a positive integer.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    _mobBuilder.SetAttribute(mobEntityId, template, property, numericValue);
                    break;

                case "protection":
                    // Parse comma or space-separated flag tokens (case-insensitive).
                    // "none" clears all flags. Unknown tokens produce an error.
                    var tokens = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var combined = ProtectionFlags.None;
                    var parseError = false;
                    foreach (var token in tokens)
                    {
                        if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
                            continue; // explicit none — combined stays None
                        if (Enum.TryParse<ProtectionFlags>(token, ignoreCase: true, out var parsed) &&
                            parsed != ProtectionFlags.None)
                        {
                            combined |= parsed;
                        }
                        else
                        {
                            await context.Output.WriteAsync(new PlainMessage(
                                $"Unknown protection flag '{token}'. Valid flags: none, untargetable, effectimmune.",
                                OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                            parseError = true;
                            break;
                        }
                    }
                    if (parseError) return;
                    _mobBuilder.SetMobProtection(mobEntityId, combined);
                    break;

                case "shop":
                    // Syntax: "off" clears the shop; "on [tillSeed] [currency]" makes/updates a
                    // shopkeeper. Base-stock rows are authored via YAML / the content editor — passing
                    // null base stock leaves any existing base stock unchanged (INV-8: the dual-write
                    // rule lives in IMobBuilderSystem.SetMobShop).
                    var shopTokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (shopTokens.Length == 0)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Usage: setmob <blueprintId> shop <off | on [tillSeed] [currency]>.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }

                    var toggle = shopTokens[0].ToLowerInvariant();
                    if (toggle is "off" or "false" or "none")
                    {
                        _mobBuilder.SetMobShop(mobEntityId, isShop: false);
                        break;
                    }
                    if (toggle is not ("on" or "true"))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown shop toggle '{shopTokens[0]}'. Use 'on' or 'off'.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }

                    long tillSeed = 0;
                    if (shopTokens.Length >= 2 && (!long.TryParse(shopTokens[1], out tillSeed) || tillSeed < 0))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Till seed must be a non-negative integer.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }

                    var currency = CurrencyId.Coin;
                    if (shopTokens.Length >= 3 &&
                        !Enum.TryParse<CurrencyId>(shopTokens[2], ignoreCase: true, out currency))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown currency '{shopTokens[2]}'.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }

                    _mobBuilder.SetMobShop(mobEntityId, isShop: true, currency, tillSeed, ratioOverride: null, baseStock: null);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, level, hp, mind, body, spirit, attunement, maxmana, maxstamina, maxastra, protection, shop.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
            }

            await _contentWriter.WriteAsync(template).ConfigureAwait(false);

            await _eventBus.PublishAsync(new MobPropertySetByAdminEvent(
                context.InvokerEntityId,
                mobEntityId,
                property,
                value)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Mob {property} set to '{value}'.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
