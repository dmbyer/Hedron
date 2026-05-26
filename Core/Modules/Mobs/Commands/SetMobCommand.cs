using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
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
        private readonly IPersistenceSystem _persistence;

        public string Name => "setmob";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on a mob.";
        public string LongDescription =>
            "Sets a property on the mob with the given blueprint id. " +
            "Valid properties: name, description, keywords (space-separated), type (none/vendor/guard/creature), " +
            "level, hp, str, dex, con.";
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
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _mobBuilder = mobBuilder;
            _contentWriter = contentWriter;
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _eventBus = eventBus;
            _persistence = persistence;
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
                    OutputSeverity.Error)).ConfigureAwait(false);
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
                    OutputSeverity.Error)).ConfigureAwait(false);
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
                            OutputSeverity.Error)).ConfigureAwait(false);
                        return;
                    }
                    _mobBuilder.SetMobType(mobEntityId, mobType);
                    break;

                case "level":
                case "hp":
                case "str":
                case "dex":
                case "con":
                    if (!int.TryParse(value, out var numericValue) || numericValue < 1)
                    {
                        await context.Output.WriteAsync(new PlainMessage(
                            "Value must be a positive integer.",
                            OutputSeverity.Error)).ConfigureAwait(false);
                        return;
                    }
                    _mobBuilder.SetAttribute(mobEntityId, template, property, numericValue);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown property '{property}'. Valid properties: name, description, keywords, type, level, hp, str, dex, con.",
                        OutputSeverity.Error)).ConfigureAwait(false);
                    return;
            }

            await _contentWriter.WriteAsync(template).ConfigureAwait(false);
            await _persistence.SaveEntityAsync(mobEntityId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new MobPropertySetByAdminEvent(
                context.InvokerEntityId,
                mobEntityId,
                property,
                value)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Mob {property} set to '{value}'.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
