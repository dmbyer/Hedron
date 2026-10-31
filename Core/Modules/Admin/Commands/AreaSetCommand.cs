using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>areaset</c> — the area member of the <c>setitem</c>/<c>setmob</c> property-setter
    /// family (<c>setarea</c> is taken: it assigns a room to an area). The builder updates the
    /// registered template and the live area entity (INV-21); the command writes the YAML and
    /// publishes <see cref="AreaPropertySetByAdminEvent"/>.
    /// </summary>
    public sealed class AreaSetCommand : ICommand
    {
        private readonly IAreaBuilderSystem _areaBuilder;
        private readonly IAreaContentWriter _contentWriter;
        private readonly IEventBus _eventBus;

        public string Name => "areaset";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on an area.";
        public string LongDescription =>
            "Sets a property on the area blueprint with the given id, updating the template and the live area. " +
            "Valid properties: name, description, " +
            "designation (\"<tier> <band>\" or \"<tier> <band>-<band>\", e.g. \"0 1-2\"; \"clear\" or \"none\" removes it).";
        public string Usage => "areaset <areaBlueprintId> <name|description|designation> <value>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the target area."),
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: name, description, designation."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "New value."),
        });

        public AreaSetCommand(IAreaBuilderSystem areaBuilder, IAreaContentWriter contentWriter, IEventBus eventBus)
        {
            _areaBuilder = areaBuilder;
            _contentWriter = contentWriter;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var blueprintId = context.Args.Get<string>("blueprintId").Trim();
            var property = context.Args.Get<string>("property").ToLowerInvariant();
            var value = context.Args.Get<string>("value").Trim();

            AreaPropertyResult result;
            string canonical;
            switch (property)
            {
                case "name":
                    result = _areaBuilder.SetAreaName(blueprintId, value);
                    canonical = value;
                    break;

                case "description":
                    result = _areaBuilder.SetAreaDescription(blueprintId, value);
                    canonical = value;
                    break;

                case "designation":
                    if (!AreaDesignation.TryParseOrClear(value, out var designation, out var parseError))
                    {
                        await ErrorAsync(context, $"Invalid designation: {parseError}").ConfigureAwait(false);
                        return;
                    }
                    result = _areaBuilder.SetAreaDesignation(blueprintId, designation);
                    canonical = designation?.ToString() ?? "clear";
                    break;

                default:
                    await ErrorAsync(context,
                        $"Unknown property '{property}'. Valid properties: name, description, designation.")
                        .ConfigureAwait(false);
                    return;
            }

            if (!result.Success || result.Template is null)
            {
                await ErrorAsync(context, result.Error ?? "Area change refused.").ConfigureAwait(false);
                return;
            }

            await _contentWriter.WriteAsync(result.Template).ConfigureAwait(false);

            await _eventBus.PublishAsync(new AreaPropertySetByAdminEvent(
                context.InvokerEntityId, result.AreaEntityId, result.AreaBlueprintId, property, canonical))
                .ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Area '{result.AreaBlueprintId}' {property} set to {canonical}.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }

        private static Task ErrorAsync(CommandContext context, string message)
            => context.Output.WriteAsync(new PlainMessage(message, OutputSeverity.Error, OutputCategory.System));
    }
}
