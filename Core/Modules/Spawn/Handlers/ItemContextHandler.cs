using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Events;

namespace Hedron.Core.Modules.Spawn.Handlers
{
    /// <summary>
    /// Manages the persistence lifecycle of item entities based on their context.
    /// Promotes items to persistent when a player picks them up (player-owned context);
    /// demotes them when dropped to the floor (world-floor context, vanishes on restart).
    ///
    /// <para>
    /// Extended for shopping (WP-2): also handles <see cref="ItemBoughtEvent"/> and
    /// <see cref="ItemSoldEvent"/> to apply the same persistence-pool transitions for trade:
    /// <list type="bullet">
    ///   <item>Buy → adds <see cref="PersistentEntity"/> (item enters player's persistent inventory),
    ///         <b>keeps</b> <c>BlueprintComponent</c> as an origin record (INV-21), removes
    ///         <see cref="ShopStockComponent"/> so the item no longer carries shop provenance.</item>
    ///   <item>Sell → removes <see cref="PersistentEntity"/> (item becomes world-transient on the
    ///         buy-back shelf, mirroring drop semantics; cleared on restart so the non-persistent
    ///         shopkeeper has no dangling references).</item>
    /// </list>
    /// </para>
    ///
    /// <para>Priority Domain — runs before broadcast handlers so the item is in the flush pool
    /// before any subsequent save-on-change logic runs.</para>
    /// </summary>
    public sealed class ItemContextHandler :
        IEventHandler<ItemPickedUpEvent>,
        IEventHandler<ItemDroppedEvent>,
        IEventHandler<ItemBoughtEvent>,
        IEventHandler<ItemSoldEvent>
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

        public Task HandleAsync(ItemBoughtEvent @event)
        {
            // Promote to persistent (item moves into player's inventory — player-owned context).
            // INV-21: BlueprintComponent is NOT cleared; it is preserved as an origin record.
            if (!_entityService.HasComponent<PersistentEntity>(@event.ItemEntityId))
                _entityService.AddComponent(@event.ItemEntityId, new PersistentEntity());

            // Remove shop provenance — the item is no longer a shop item.
            _entityService.RemoveComponent<ShopStockComponent>(@event.ItemEntityId);

            return Task.CompletedTask;
        }

        public Task HandleAsync(ItemSoldEvent @event)
        {
            // Demote to world-transient (item moves onto the buy-back shelf — world-content context;
            // mirroring the drop-to-ground lifecycle). Cleared on restart so the non-persistent
            // shopkeeper has no dangling references (see persistence opt-in audit in shopping.md).
            _entityService.RemoveComponent<PersistentEntity>(@event.ItemEntityId);
            return Task.CompletedTask;
        }
    }
}
