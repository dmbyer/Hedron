using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// Admin verb <c>setmob</c>. Sets a property on a mob <b>blueprint</b>: the builder updates the
    /// template and every live world instance (never a <c>PersistentEntity</c> copy, INV-21); with no
    /// live instances only the template changes. The command writes the YAML and publishes
    /// <see cref="MobPropertySetByAdminEvent"/> with the instance count.
    /// </summary>
    public sealed class SetMobCommand : ICommand
    {
        private readonly IMobBuilderSystem _mobBuilder;
        private readonly IMobContentWriter _contentWriter;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IEventBus _eventBus;

        public string Name => "setmob";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on a mob.";
        public string LongDescription =>
            "Sets a property on the mob blueprint with the given id, updating the template and every " +
            "live world instance of it. " +
            "Valid properties: name, description, keywords (space-separated), type (none/vendor/guard/creature), " +
            "level, hp, mind, body, spirit, attunement, maxmana, maxstamina, maxastra, " +
            "protection (comma or space-separated flags: none, untargetable, effectimmune), " +
            "tier (Ascension tier tag, integer 0-6, 0 = unbanded/base), " +
            "band (descriptive Band tag, integer 0-3, 0 = unbanded), " +
            "xpscale (per-mob kill-experience scale, non-negative number, 1.0 = default, 0 = awards nothing), " +
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
            ITemplateRegistry templateRegistry,
            IEventBus eventBus)
        {
            _mobBuilder = mobBuilder;
            _contentWriter = contentWriter;
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

            var instances = 0;
            switch (property)
            {
                case "name":
                    instances = _mobBuilder.SetMobName(blueprintId, value);
                    break;

                case "description":
                    instances = _mobBuilder.SetMobDescription(blueprintId, value);
                    break;

                case "keywords":
                    var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    instances = _mobBuilder.SetMobKeywords(blueprintId, keywords);
                    break;

                case "type":
                    if (!Enum.TryParse<MobType>(value, ignoreCase: true, out var mobType))
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            $"Unknown mob type '{value}'. Valid types: none, vendor, guard, creature.",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _mobBuilder.SetMobType(blueprintId, mobType);
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
                    instances = _mobBuilder.SetAttribute(blueprintId, property, numericValue);
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
                    instances = _mobBuilder.SetMobProtection(blueprintId, combined);
                    break;

                case "tier":
                    if (!int.TryParse(value, out var tier) || tier < 0 || tier > 6)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Tier must be an integer 0-6 (0 = unbanded/base).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _mobBuilder.SetMobTier(blueprintId, tier);
                    break;

                case "band":
                    if (!int.TryParse(value, out var band) || band < 0 || band > 3)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Band must be an integer 0-3 (0 = unbanded).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _mobBuilder.SetMobBand(blueprintId, band);
                    break;

                case "xpscale":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var xpScale) ||
                        xpScale < 0.0)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Xpscale must be a non-negative number (1.0 = default, 0 = this mob awards no experience).",
                            OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                        return;
                    }
                    instances = _mobBuilder.SetMobXpScale(blueprintId, xpScale);
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
                        instances = _mobBuilder.SetMobShop(blueprintId, isShop: false);
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

                    instances = _mobBuilder.SetMobShop(blueprintId, isShop: true, currency, tillSeed, ratioOverride: null, baseStock: null);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, level, hp, mind, body, spirit, attunement, maxmana, maxstamina, maxastra, protection, tier, band, xpscale, shop.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
            }

            await _contentWriter.WriteAsync(template).ConfigureAwait(false);

            await _eventBus.PublishAsync(new MobPropertySetByAdminEvent(
                context.InvokerEntityId,
                template.BlueprintId,
                property,
                value,
                instances)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Mob {property} set to '{value}' ({instances} live instance(s) updated).",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
