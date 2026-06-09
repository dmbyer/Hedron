using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>dig &lt;direction&gt; [name]</c>.
    /// Creates a new room in the named direction, wires bidirectional exits, and auto-moves
    /// the administrator into the new room. Replaces the slice-2 connect-to-existing behaviour.
    /// </summary>
    public sealed class DigCommand : ICommand
    {
        private readonly IRoomBuilderSystem _roomBuilder;
        private readonly IRoomContentWriter _contentWriter;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IAreaSystem _areaSystem;

        public string Name => "dig";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Dig a new room in the given direction and move into it.";
        public string LongDescription =>
            "Creates a new room entity in the named direction from your current position, " +
            "wires bidirectional exits, and moves you into the new room.";
        public string Usage => "dig <direction> [name]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("direction", typeof(Direction), CommandArgumentKind.Token,
                Required: true, "Direction to dig (north, south, east, west, up, down)."),
            new CommandArgument("name", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name for the new room (default: \"New Room\")."),
        });

        public DigCommand(
            IRoomBuilderSystem roomBuilder,
            IRoomContentWriter contentWriter,
            ITemplateRegistry templateRegistry,
            EntityService entityService,
            IEventBus eventBus,
            IAreaSystem areaSystem)
        {
            _roomBuilder = roomBuilder;
            _contentWriter = contentWriter;
            _templateRegistry = templateRegistry;
            _entityService = entityService;
            _eventBus = eventBus;
            _areaSystem = areaSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var direction = context.Args.Get<Direction>("direction");
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "New Room";

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(new PlainMessage("You have no location.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var sourceRoomId = location.RoomEntityId;
            if (!_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
            {
                await context.Output.WriteAsync(new PlainMessage("Your current location is not a room.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (sourceRoom.Exits.ContainsKey(direction))
            {
                await context.Output.WriteAsync(new PlainMessage($"An exit already exists in that direction.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Inherit the source room's area if it has one.
            string areaId = "";
            var sourceAreaEntityId = _areaSystem.GetAreaForRoom(sourceRoomId);
            if (sourceAreaEntityId.HasValue &&
                _entityService.TryGet<BlueprintComponent>(sourceAreaEntityId.Value, out var areaBp))
                areaId = areaBp.BlueprintId;

            var result = _roomBuilder.CreateRoom(name, areaId: areaId);
            _roomBuilder.LinkExits(sourceRoomId, direction, result.RoomEntityId, bidirectional: true);

            location.RoomEntityId = result.RoomEntityId;
            location.RoomBlueprintId = result.BlueprintId;

            await _eventBus.PublishAsync(new RoomCreatedByAdminEvent(
                context.InvokerEntityId,
                result.RoomEntityId,
                result.BlueprintId,
                sourceRoomId,
                direction,
                BidirectionalLinkCreated: true)).ConfigureAwait(false);

            // Write YAML for the new room. Also re-write the source room's YAML because
            // LinkExits added a new exit to its template's Exits map.
            // The YAML file is the room's only durable state — no SaveEntityAsync needed.
            if (_templateRegistry.TryGet(result.BlueprintId, out var newTpl) &&
                newTpl is RoomTemplate newRoomTpl)
                await _contentWriter.WriteAsync(newRoomTpl).ConfigureAwait(false);

            if (_entityService.TryGet<BlueprintComponent>(sourceRoomId, out var srcBp) &&
                _templateRegistry.TryGet(srcBp.BlueprintId, out var srcTpl) &&
                srcTpl is RoomTemplate srcRoomTpl)
                await _contentWriter.WriteAsync(srcRoomTpl).ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerMovedEvent(
                context.InvokerEntityId,
                sourceRoomId,
                result.RoomEntityId,
                direction)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Room '{name}' ({result.BlueprintId}) created to the {direction.ToString().ToLower()}.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
