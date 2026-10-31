using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Components;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin command <c>area [blueprintId]</c>.
    /// With no argument, inspects the area of the invoker's current room.
    /// With a blueprint id, inspects the named area entity.
    /// No events published — read-only inspector.
    /// </summary>
    public sealed class AreaCommand : ICommand
    {
        private readonly IAreaSystem _areaSystem;
        private readonly IAreaPlacementSystem _placementSystem;
        private readonly EntityService _entityService;

        public string Name => "area";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Inspect an area entity.";
        public string LongDescription =>
            "Without an argument, shows the area for the room you are currently in. " +
            "With a blueprint id, inspects the named area entity. Shows the area's designation and any " +
            "spawn rules placing a mob or item outside it.";
        public string Usage => "area [blueprintId]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: false, "Area blueprint id to inspect (omit to use current room's area)."),
        });

        public AreaCommand(IAreaSystem areaSystem, IAreaPlacementSystem placementSystem, EntityService entityService)
        {
            _areaSystem = areaSystem;
            _placementSystem = placementSystem;
            _entityService = entityService;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            uint areaEntityId;
            string areaBlueprintId;

            context.Args.TryGet<string>("blueprintId", out var blueprintIdArg);

            if (string.IsNullOrWhiteSpace(blueprintIdArg))
            {
                // Resolve via current room.
                if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
                {
                    await context.Output.WriteAsync(new PlainMessage(
                        "You have no location.", OutputSeverity.Error, OutputCategory.System))
                        .ConfigureAwait(false);
                    return;
                }

                var roomAreaId = _areaSystem.GetAreaForRoom(location.RoomEntityId);
                if (roomAreaId == null)
                {
                    await context.Output.WriteAsync(new PlainMessage(
                        "This room is not assigned to an area.", OutputSeverity.System, OutputCategory.System))
                        .ConfigureAwait(false);
                    return;
                }

                areaEntityId = roomAreaId.Value;
                // Resolve blueprint id from the area entity's BlueprintComponent.
                areaBlueprintId = _entityService.TryGet<BlueprintComponent>(areaEntityId, out var bp)
                    ? bp.BlueprintId
                    : areaEntityId.ToString();
            }
            else
            {
                // Resolve by blueprint id scan.
                var found = false;
                areaEntityId = 0;
                areaBlueprintId = blueprintIdArg.Trim();

                foreach (var (entityId, bpComp) in _entityService.GetAllComponents<BlueprintComponent>())
                {
                    if (string.Equals(bpComp.BlueprintId, areaBlueprintId, StringComparison.OrdinalIgnoreCase))
                    {
                        areaEntityId = entityId;
                        areaBlueprintId = bpComp.BlueprintId; // canonical casing for the findings filter
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Area not found: {areaBlueprintId}", OutputSeverity.Error, OutputCategory.System))
                        .ConfigureAwait(false);
                    return;
                }
            }

            if (!_entityService.TryGet<AreaComponent>(areaEntityId, out var areaComponent))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "Entity is not an area.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Area: {areaComponent.Name} ({areaBlueprintId})");
            sb.AppendLine($"  {areaComponent.Description}");

            if (_entityService.TryGet<AspectAffinitiesComponent>(areaEntityId, out var affinities) &&
                affinities.AffinityWeights.Count > 0)
            {
                var affinityParts = new List<string>();
                foreach (var (aspect, weight) in affinities.AffinityWeights)
                    affinityParts.Add($"{aspect} {weight}%");
                sb.AppendLine($"  Aspect Affinities: {string.Join(", ", affinityParts)}");
            }

            // Designation (live component) and its out-of-designation findings (R6) — the
            // findings come from the shared judgement over the registry, never computed here.
            sb.AppendLine(_entityService.TryGet<AreaDesignationComponent>(areaEntityId, out var designation)
                ? $"  Designation: {designation.Designation}"
                : "  Designation: undesignated");
            var findings = _placementSystem.Findings(areaBlueprintId);
            if (findings.Count == 0)
                sb.AppendLine("  Out-of-designation placements: none");
            else
            {
                sb.AppendLine($"  Out-of-designation placements ({findings.Count}):");
                foreach (var finding in findings)
                    sb.AppendLine($"    {finding.Describe()}");
            }

            var rooms = _areaSystem.GetRoomsInArea(areaEntityId);
            sb.AppendLine($"  Rooms ({rooms.Count}):");
            foreach (var roomEntityId in rooms)
            {
                var roomName = _entityService.TryGet<RoomComponent>(roomEntityId, out var room)
                    ? room.Name
                    : $"#{roomEntityId}";
                var roomBpId = _entityService.TryGet<BlueprintComponent>(roomEntityId, out var roomBp)
                    ? roomBp.BlueprintId
                    : roomEntityId.ToString();
                sb.AppendLine($"    {roomName} ({roomBpId})");
            }

            await context.Output.WriteAsync(new PlainMessage(
                sb.ToString().TrimEnd(), OutputSeverity.System, OutputCategory.Info))
                .ConfigureAwait(false);
        }
    }
}
