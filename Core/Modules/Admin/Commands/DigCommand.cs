using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>dig &lt;direction&gt; &lt;targetRoomBlueprintId&gt;</c>.
    /// Adds an exit and wires the reverse link. Source YAML is not rewritten;
    /// durability comes from <c>PersistenceSystem</c>. Privilege enforced by dispatcher.
    /// </summary>
    public sealed class DigCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "dig";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Dig a new exit from the current room.";
        public string LongDescription => "Adds an exit from your current room to the target room and wires a reverse link. " +
            "The source YAML is not rewritten; durability comes from the persistence flush.";
        public string Usage => "dig <direction> <targetRoomBlueprintId>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("direction", typeof(Direction), CommandArgumentKind.Token,
                Required: true, "Direction to dig (north, south, east, west, up, down)."),
            new CommandArgument("targetRoomBlueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the destination room."),
        });

        public DigCommand(ITemplateRegistry templateRegistry, EntityService entityService, IEventBus eventBus)
        {
            _templateRegistry = templateRegistry;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var direction = context.Args.Get<Direction>("direction");
            var targetBlueprintId = context.Args.Get<string>("targetRoomBlueprintId");

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var sourceRoomId = location.RoomEntityId;
            if (!_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("Your current location is not a room.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var targetRoomId = ResolveRoomEntityId(targetBlueprintId);
            if (targetRoomId is null)
            {
                await context.Output.WriteAsync(
                    new PlainMessage($"Cannot resolve target room: {targetBlueprintId}", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            sourceRoom.Exits[direction] = targetRoomId.Value;
            UpdateTemplate(sourceRoomId, direction, targetBlueprintId);

            var bidirectional = false;
            var opposite = Opposite(direction);
            if (opposite is not null
                && _entityService.TryGet<RoomComponent>(targetRoomId.Value, out var targetRoom)
                && !targetRoom.Exits.ContainsKey(opposite.Value))
            {
                targetRoom.Exits[opposite.Value] = sourceRoomId;
                bidirectional = true;

                if (_entityService.TryGet<BlueprintComponent>(sourceRoomId, out var sourceBlueprint))
                    UpdateTemplate(targetRoomId.Value, opposite.Value, sourceBlueprint.BlueprintId);
            }

            await context.Output.WriteAsync(new PlainMessage(
                $"Dug {direction.ToString().ToLower()} → {targetBlueprintId}" +
                (bidirectional ? " (reverse exit linked)." : "."),
                OutputSeverity.Confirmation)).ConfigureAwait(false);

            await _eventBus.PublishAsync(new RoomExitAuthoredByAdminEvent(
                context.InvokerEntityId,
                sourceRoomId,
                direction,
                targetRoomId.Value,
                bidirectional)).ConfigureAwait(false);
        }

        private uint? ResolveRoomEntityId(string blueprintId)
        {
            foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(blueprint.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase)
                    && _entityService.HasComponent<RoomComponent>(entityId))
                    return entityId;
            }
            return null;
        }

        private void UpdateTemplate(uint roomEntityId, Direction direction, string targetBlueprintId)
        {
            if (!_entityService.TryGet<BlueprintComponent>(roomEntityId, out var blueprint)) return;
            if (!_templateRegistry.TryGet(blueprint.BlueprintId, out var template)) return;
            if (template is RoomTemplate roomTemplate)
                roomTemplate.Exits[direction] = targetBlueprintId;
        }

        private static Direction? Opposite(Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _               => null,
        };
    }
}
