using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World.Events;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Admin.Handlers
{
    /// <summary>
    /// Cross-cutting handler that writes one structured-log entry per admin action.
    /// Subscribes to all four admin events at <see cref="HandlerPriority.Notification"/>,
    /// after gameplay handlers and before persistence dirty-marking.
    /// </summary>
    /// <remarks>
    /// No dedicated audit-file sink in this slice — promotion to a separate ops sink is a
    /// future concern. The structured log uses a stable event name
    /// (<c>AdminCommandExecuted</c>) so log scrapers can filter without parsing free text.
    /// </remarks>
    public sealed class AdminAuditHandler :
        IEventHandler<EntitySpawnedByAdminEvent>,
        IEventHandler<PlayerTeleportedByAdminEvent>,
        IEventHandler<RoomExitAuthoredByAdminEvent>,
        IEventHandler<RoomCreatedByAdminEvent>,
        IEventHandler<RoomPropertySetByAdminEvent>,
        IEventHandler<ContentReloadedEvent>
    {
        private readonly EntityService _entityService;
        private readonly ILogger<AdminAuditHandler> _logger;

        public int Priority => HandlerPriority.Notification;

        public AdminAuditHandler(EntityService entityService, ILogger<AdminAuditHandler> logger)
        {
            _entityService = entityService;
            _logger = logger;
        }

        public Task HandleAsync(EntitySpawnedByAdminEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: admin={Admin} command=spawn blueprint={BlueprintId} entity={EntityId} room={RoomEntityId}",
                ResolveName(e.AdminEntityId), e.BlueprintId, e.SpawnedEntityId, e.RoomEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PlayerTeleportedByAdminEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: admin={Admin} command=teleport target={Target} from={From} to={To}",
                ResolveName(e.AdminEntityId), e.TargetEntityId, e.FromRoomEntityId, e.ToRoomEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RoomExitAuthoredByAdminEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: admin={Admin} command=dig room={RoomEntityId} direction={Direction} target={TargetRoomEntityId} bidirectional={Bidirectional}",
                ResolveName(e.AdminEntityId), e.RoomEntityId, e.Direction, e.TargetRoomEntityId, e.BidirectionalLinkCreated);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RoomCreatedByAdminEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: admin={Admin} command=dig newRoom={NewRoomEntityId} blueprint={BlueprintId} sourceRoom={SourceRoomEntityId} direction={Direction} bidirectional={Bidirectional}",
                ResolveName(e.AdminEntityId), e.NewRoomEntityId, e.BlueprintId, e.SourceRoomEntityId, e.Direction, e.BidirectionalLinkCreated);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RoomPropertySetByAdminEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: admin={Admin} command=set room={RoomEntityId} property={PropertyName} value={NewValue}",
                ResolveName(e.AdminEntityId), e.RoomEntityId, e.PropertyName, e.NewValue);
            return Task.CompletedTask;
        }

        public Task HandleAsync(ContentReloadedEvent e)
        {
            _logger.LogInformation(
                "AdminCommandExecuted: command=reload loaded={Loaded} unchanged={Unchanged} removed={Removed}",
                e.TemplatesLoaded, e.TemplatesUnchanged, e.TemplatesRemoved);
            return Task.CompletedTask;
        }

        private string ResolveName(uint entityId)
        {
            return _entityService.TryGet<PlayerComponent>(entityId, out var p)
                ? p.DisplayName
                : $"#{entityId}";
        }
    }
}
