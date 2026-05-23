using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Systems;

namespace Hedron.Core.Handlers
{
    /// <summary>
    /// Marks entities dirty whenever a state-change event mutates <c>[Persistent]</c> data.
    /// Cross-cutting handler — lives at <c>Core/Handlers/</c> and subscribes to events
    /// from any module. Priority 90 (<see cref="HandlerPriority.Persistence"/>).
    /// </summary>
    /// <remarks>
    /// Currently subscribed events:
    /// <list type="bullet">
    ///   <item><see cref="EntitySpawnedByAdminEvent"/> — slice 2</item>
    ///   <item><see cref="RoomExitAuthoredByAdminEvent"/> — slice 2</item>
    ///   <item><see cref="RoomCreatedByAdminEvent"/> — slice 5a; mark both new and source rooms dirty</item>
    ///   <item><see cref="RoomPropertySetByAdminEvent"/> — slice 5a; mark the mutated room dirty</item>
    ///   <item><see cref="AccountCreatedEvent"/> — slice 5; mark the new account entity dirty</item>
    ///   <item><see cref="CharacterCreatedEvent"/> — slice 5; mark the new character entity dirty</item>
    ///   <item><see cref="PlayerDisconnectedEvent"/> — slice 5; ensure character state is flushed on logout</item>
    ///   <item><see cref="PlayerMovedEvent"/> — mark character dirty so LocationComponent is saved each flush cycle</item>
    ///   <item><see cref="PlayerTeleportedByAdminEvent"/> — mark the moved target dirty</item>
    /// </list>
    /// </remarks>
    public sealed class PersistenceHandler :
        IEventHandler<EntitySpawnedByAdminEvent>,
        IEventHandler<RoomExitAuthoredByAdminEvent>,
        IEventHandler<RoomCreatedByAdminEvent>,
        IEventHandler<RoomPropertySetByAdminEvent>,
        IEventHandler<AccountCreatedEvent>,
        IEventHandler<CharacterCreatedEvent>,
        IEventHandler<PlayerDisconnectedEvent>,
        IEventHandler<PlayerMovedEvent>,
        IEventHandler<PlayerTeleportedByAdminEvent>
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

        public Task HandleAsync(RoomCreatedByAdminEvent e)
        {
            MarkIfPersistent(e.NewRoomEntityId);
            MarkIfPersistent(e.SourceRoomEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RoomPropertySetByAdminEvent e)
        {
            MarkIfPersistent(e.RoomEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(AccountCreatedEvent e)
        {
            _persistence.MarkDirty(e.AccountEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(CharacterCreatedEvent e)
        {
            _persistence.MarkDirty(e.CharacterEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PlayerDisconnectedEvent e)
        {
            MarkIfPersistent(e.PlayerEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PlayerMovedEvent e)
        {
            _persistence.MarkDirty(e.PlayerEntityId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(PlayerTeleportedByAdminEvent e)
        {
            _persistence.MarkDirty(e.TargetEntityId);
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
