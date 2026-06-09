using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin command <c>setarea &lt;roomBlueprintId&gt; &lt;areaBlueprintId&gt;</c>.
    /// Assigns an existing room entity to an existing area entity.
    /// Publishes <see cref="RoomAreaAssignedByAdminEvent"/> on success.
    /// </summary>
    public sealed class SetAreaCommand : ICommand
    {
        private readonly IAreaSystem _areaSystem;
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IRoomContentWriter _contentWriter;
        private readonly IEventBus _eventBus;

        public string Name => "setarea";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Assign a room to an area.";
        public string LongDescription => "Assigns a room entity to an area entity by blueprint id.";
        public string Usage => "setarea <roomBlueprintId> <areaBlueprintId>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("roomBlueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the room to reassign."),
            new CommandArgument("areaBlueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "Blueprint id of the target area."),
        });

        public SetAreaCommand(
            IAreaSystem areaSystem,
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IRoomContentWriter contentWriter,
            IEventBus eventBus)
        {
            _areaSystem = areaSystem;
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _contentWriter = contentWriter;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var roomBlueprintId = context.Args.Get<string>("roomBlueprintId").Trim();
            var areaBlueprintId = context.Args.Get<string>("areaBlueprintId").Trim();

            // Resolve room entity.
            uint roomEntityId = 0;
            foreach (var (entityId, bpComp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bpComp.BlueprintId, roomBlueprintId, StringComparison.OrdinalIgnoreCase))
                {
                    roomEntityId = entityId;
                    break;
                }
            }

            if (roomEntityId == 0 || !_entityService.HasComponent<RoomComponent>(roomEntityId))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Room not found: {roomBlueprintId}", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Resolve area entity.
            uint areaEntityId = 0;
            foreach (var (entityId, bpComp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bpComp.BlueprintId, areaBlueprintId, StringComparison.OrdinalIgnoreCase))
                {
                    areaEntityId = entityId;
                    break;
                }
            }

            if (areaEntityId == 0 || !_entityService.HasComponent<AreaComponent>(areaEntityId))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Area not found: {areaBlueprintId}", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Assign room to area.
            _areaSystem.AssignRoomToArea(roomEntityId, areaEntityId, areaBlueprintId);

            // Persist room template update.
            if (_templateRegistry.TryGet(roomBlueprintId, out var tpl) && tpl is RoomTemplate roomTemplate)
                await _contentWriter.WriteAsync(roomTemplate).ConfigureAwait(false);

            // Publish audit event.
            await _eventBus.PublishAsync(new RoomAreaAssignedByAdminEvent(
                context.InvokerEntityId,
                roomEntityId,
                roomBlueprintId,
                areaEntityId,
                areaBlueprintId)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Room '{roomBlueprintId}' assigned to area '{areaBlueprintId}'.",
                OutputSeverity.Confirmation, OutputCategory.System))
                .ConfigureAwait(false);
        }
    }
}
