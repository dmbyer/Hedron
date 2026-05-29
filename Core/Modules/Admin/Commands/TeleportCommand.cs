using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>teleport &lt;target&gt;</c>. Target is a room blueprint id or player name.
    /// Updates the invoker's <see cref="LocationComponent"/> and publishes
    /// <see cref="PlayerTeleportedByAdminEvent"/>. Privilege is enforced by the dispatcher.
    /// </summary>
    public sealed class TeleportCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "teleport";
        public IReadOnlyList<string> Aliases { get; } = new[] { "tp" };
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Teleport yourself to a room or player.";
        public string LongDescription => "Teleports you to the specified target. " +
            "Target can be a room blueprint id (e.g. room.crossroads) or a player's display name.";
        public string Usage => "teleport <roomBlueprintId|playerName>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.Token,
                Required: true, "Room blueprint id or player display name."),
        });

        public TeleportCommand(ITemplateRegistry templateRegistry, EntityService entityService, IEventBus eventBus)
        {
            _templateRegistry = templateRegistry;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var target = context.Args.Get<string>("target");

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var fromRoomId = location.RoomEntityId;
            var resolvedRoomId = ResolveRoomEntityId(target);
            if (resolvedRoomId is null)
            {
                await context.Output.WriteAsync(
                    new PlainMessage($"Cannot resolve teleport target: {target}", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            location.RoomEntityId = resolvedRoomId.Value;
            location.RoomBlueprintId = _entityService.TryGet<BlueprintComponent>(resolvedRoomId.Value, out var destBp)
                ? destBp.BlueprintId
                : null;

            await context.Output.WriteAsync(
                new PlainMessage($"Teleported to entity #{resolvedRoomId.Value}.", OutputSeverity.Confirmation))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new PlayerTeleportedByAdminEvent(
                context.InvokerEntityId,
                context.InvokerEntityId,
                fromRoomId,
                resolvedRoomId.Value)).ConfigureAwait(false);
        }

        private uint? ResolveRoomEntityId(string target)
        {
            if (_templateRegistry.TryGet(target, out _))
            {
                foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
                {
                    if (string.Equals(blueprint.BlueprintId, target, StringComparison.OrdinalIgnoreCase)
                        && _entityService.HasComponent<RoomComponent>(entityId))
                        return entityId;
                }
            }

            foreach (var (entityId, player) in _entityService.GetAllComponents<PlayerComponent>())
            {
                if (string.Equals(player.DisplayName, target, StringComparison.OrdinalIgnoreCase)
                    && _entityService.TryGet<LocationComponent>(entityId, out var theirLoc))
                    return theirLoc.RoomEntityId;
            }

            return null;
        }
    }
}
