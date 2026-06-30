using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin command <c>listents &lt;area|room&gt;</c>.
    /// Prints a tabular view of all entities of a given type.
    /// No events published — read-only inspector.
    /// (Named <c>listents</c> rather than <c>list</c> so the player shop-browse verb
    /// <c>list</c> — <see cref="Hedron.Core.Modules.Shopping.Commands.ListCommand"/> — owns
    /// the unqualified verb.)
    /// </summary>
    public sealed class ListEntitiesCommand : ICommand
    {
        private readonly EntityService _entityService;

        public string Name => "listents";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "List entities of a given type.";
        public string LongDescription => "Lists all entities of a given type (area or room) in a tabular view. No events fired.";
        public string Usage => "listents <area|room>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("type", typeof(string), CommandArgumentKind.Token,
                Required: true, "Entity type to list: area or room."),
        });

        public ListEntitiesCommand(EntityService entityService)
        {
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("type", out var typeToken);
            var token = typeToken?.Trim().ToLowerInvariant() ?? string.Empty;

            if (token != "area" && token != "room")
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Unknown type '{typeToken}'. Accepted: area, room.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Name             | ShortDesc       | BlueprintId");
            sb.AppendLine("-----------------+-----------------+------------");

            if (token == "area")
            {
                foreach (var (entityId, component) in _entityService.GetAllComponents<AreaComponent>())
                {
                    var name = component.Name ?? string.Empty;
                    var desc = component.Description ?? string.Empty;
                    var shortDesc = desc.Length > 15 ? desc[..15] + "…" : desc;
                    var bpId = _entityService.TryGet<BlueprintComponent>(entityId, out var bp)
                        ? bp.BlueprintId
                        : entityId.ToString();
                    sb.AppendLine($"{name,-17}| {shortDesc,-17}| {bpId}");
                }
            }
            else
            {
                foreach (var (entityId, component) in _entityService.GetAllComponents<RoomComponent>())
                {
                    var name = component.Name ?? string.Empty;
                    var desc = component.Description ?? string.Empty;
                    var shortDesc = desc.Length > 15 ? desc[..15] + "…" : desc;
                    var bpId = _entityService.TryGet<BlueprintComponent>(entityId, out var bp)
                        ? bp.BlueprintId
                        : entityId.ToString();
                    sb.AppendLine($"{name,-17}| {shortDesc,-17}| {bpId}");
                }
            }

            await context.Output.WriteAsync(new PlainMessage(
                sb.ToString().TrimEnd(), OutputSeverity.System, OutputCategory.Info)).ConfigureAwait(false);
        }
    }
}
