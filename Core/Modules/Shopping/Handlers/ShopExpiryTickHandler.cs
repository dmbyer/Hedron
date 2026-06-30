using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Shopping.Handlers
{
    /// <summary>
    /// Interval-gated heartbeat sweep that destroys buy-back shelf items whose
    /// <see cref="ShopStockComponent.ExpiresAt"/> has passed.
    ///
    /// <para>
    /// On each <see cref="HeartbeatTickEvent"/>, accumulates <c>Elapsed</c> against
    /// <see cref="ShopOptions.BuyBackRetention"/>. When the accumulated total reaches or exceeds
    /// the interval, iterates every entity with <see cref="ShopComponent"/>, calls
    /// <see cref="IShopSystem.FindExpired"/> with the injected clock's current time, then calls
    /// <see cref="EntityService.DestroyEntity"/> for each expired acquired item. Base-stock items
    /// are never touched. Publishes nothing — closed sweep with no game-rule fan-out (INV-10).
    /// </para>
    ///
    /// <para>Priority: <see cref="HandlerPriority.Domain"/> (20).</para>
    ///
    /// <para>
    /// The two tick handlers (<see cref="ShopRestockTickHandler"/> and this one) are mutually
    /// independent — no ordering constraint between them.
    /// </para>
    /// </summary>
    public sealed class ShopExpiryTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly IShopSystem _shopSystem;
        private readonly IClock _clock;
        private readonly TimeSpan _expiryInterval;

        private TimeSpan _accumulated = TimeSpan.Zero;

        public int Priority => HandlerPriority.Domain;

        public ShopExpiryTickHandler(
            EntityService entityService,
            IShopSystem shopSystem,
            IClock clock,
            IOptions<ShopOptions> options)
        {
            _entityService = entityService;
            _shopSystem = shopSystem;
            _clock = clock;
            _expiryInterval = options.Value.BuyBackRetention;
        }

        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            _accumulated += @event.Elapsed;
            if (_accumulated < _expiryInterval)
                return Task.CompletedTask;

            _accumulated = TimeSpan.Zero;

            var nowUtc = _clock.UtcNow;

            // Snapshot shop ids before iterating — avoid mutation during enumeration.
            var shopIds = _entityService.GetAllComponents<ShopComponent>()
                .Select(p => p.EntityId)
                .ToList();

            foreach (var shopId in shopIds)
            {
                var expiredIds = _shopSystem.FindExpired(shopId, nowUtc);
                foreach (var itemId in expiredIds)
                    _entityService.DestroyEntity(itemId);
            }

            return Task.CompletedTask;
        }
    }
}
