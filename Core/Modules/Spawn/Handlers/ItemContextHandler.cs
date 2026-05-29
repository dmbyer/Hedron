using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;

namespace Hedron.Core.Modules.Spawn.Handlers
{
    /// <summary>
    /// Manages the persistence lifecycle of item entities based on their context.
    /// Promotes items to persistent when a player picks them up (player-owned context);
    /// demotes them when dropped to the floor (world-floor context, vanishes on restart).
    /// Priority Domain — runs before broadcast handlers so the item is in the flush pool
    /// before any subsequent save-on-change logic runs.
    /// </summary>
    public sealed class ItemContextHandler :
        IEventHandler<ItemPickedUpEvent>,
        IEventHandler<ItemDroppedEvent>
    {
        private readonly EntityService _entityService;

        public int Priority => HandlerPriority.Domain;

        public ItemContextHandler(EntityService entityService)
        {
            _entityService = entityService;
        }

        public Task HandleAsync(ItemPickedUpEvent @event)
        {
            if (!_entityService.HasComponent<PersistentEntity>(@event.ItemEntityId))
                _entityService.AddComponent(@event.ItemEntityId, new PersistentEntity());
            return Task.CompletedTask;
        }

        public Task HandleAsync(ItemDroppedEvent @event)
        {
            _entityService.RemoveComponent<PersistentEntity>(@event.ItemEntityId);
            return Task.CompletedTask;
        }
    }
}
