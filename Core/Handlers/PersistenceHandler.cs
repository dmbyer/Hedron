using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Systems;

namespace Hedron.Core.Handlers
{
    /// <summary>
    /// Marks entities dirty whenever a state-change event mutates <c>[Persistent]</c> data.
    /// Cross-cutting handler — lives at <c>Core/Handlers/</c> and may subscribe to events
    /// from any module (matching the <c>NotificationHandler</c> precedent in
    /// <c>docs/reference/handlers.md</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Priority <see cref="HandlerPriority.Persistence"/> (90) — runs after domain processing
    /// (priority 20) and notification (priority 80) so dirty-marking sees the final state.
    /// </para>
    /// <para>
    /// <b>Currently subscribed events</b> (kept in sync with <c>Server/Program.cs</c>):
    /// <list type="bullet">
    ///   <item><see cref="EntitySpawnedByAdminEvent"/> — slice 2; mark the new entity dirty
    ///         if it carries any <c>[Persistent]</c> component.</item>
    ///   <item><see cref="RoomExitAuthoredByAdminEvent"/> — slice 2; mark the source room,
    ///         and the target room when the link is bidirectional.</item>
    /// </list>
    /// New slices add their events here and re-subscribe in <c>Server/Program.cs</c>.
    /// </para>
    /// </remarks>
    public sealed class PersistenceHandler :
        IEventHandler<EntitySpawnedByAdminEvent>,
        IEventHandler<RoomExitAuthoredByAdminEvent>
    {
        private readonly IPersistenceSystem _persistence;
        private readonly IComponentTypeRegistry _typeRegistry;
        private readonly EntityService _entityService;

        public int Priority => HandlerPriority.Persistence;

        public PersistenceHandler(
            IPersistenceSystem persistence,
            IComponentTypeRegistry typeRegistry,
            EntityService entityService)
        {
            _persistence = persistence;
            _typeRegistry = typeRegistry;
            _entityService = entityService;
        }

        public Task HandleAsync(EntitySpawnedByAdminEvent e)
        {
            MarkIfPersistent(e.SpawnedEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RoomExitAuthoredByAdminEvent e)
        {
            MarkIfPersistent(e.RoomEntityId);
            if (e.BidirectionalLinkCreated)
                MarkIfPersistent(e.TargetRoomEntityId);
            return Task.CompletedTask;
        }

        private void MarkIfPersistent(uint entityId)
        {
            foreach (var (componentType, _) in _entityService.GetAllComponentsForEntity(entityId))
            {
                if (_typeRegistry.IsPersistent(componentType))
                {
                    _persistence.MarkDirty(entityId);
                    return;
                }
            }
        }
    }
}
