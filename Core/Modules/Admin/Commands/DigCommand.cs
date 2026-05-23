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
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

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
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _roomBuilder = roomBuilder;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var direction = context.Args.Get<Direction>("direction");
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "New Room";

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var sourceRoomId = location.RoomEntityId;
            if (!_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
            {
                await context.Output.WriteAsync(new PlainMessage("Your current location is not a room.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            if (sourceRoom.Exits.ContainsKey(direction))
            {
                await context.Output.WriteAsync(new PlainMessage($"An exit already exists in that direction.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var result = _roomBuilder.CreateRoom(name);
            _roomBuilder.LinkExits(sourceRoomId, direction, result.RoomEntityId, bidirectional: true);

            location.RoomEntityId = result.RoomEntityId;

            await _eventBus.PublishAsync(new RoomCreatedByAdminEvent(
                context.InvokerEntityId,
                result.RoomEntityId,
                result.BlueprintId,
                sourceRoomId,
                direction,
                BidirectionalLinkCreated: true)).ConfigureAwait(false);

            // Save both rooms immediately — admin content must be durable without waiting
            // for a flush cycle.
            await _persistence.SaveEntityAsync(result.RoomEntityId).ConfigureAwait(false);
            await _persistence.SaveEntityAsync(sourceRoomId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerMovedEvent(
                context.InvokerEntityId,
                sourceRoomId,
                result.RoomEntityId,
                direction)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Room '{name}' ({result.BlueprintId}) created to the {direction.ToString().ToLower()}.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
