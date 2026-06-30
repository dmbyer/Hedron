using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Shopping.Handlers
{
    /// <summary>
    /// Interval-gated heartbeat sweep that tops up each shop's base stock to its authored levels.
    ///
    /// <para>
    /// On each <see cref="HeartbeatTickEvent"/>, accumulates <c>Elapsed</c> against
    /// <see cref="ShopOptions.RestockInterval"/>. When the accumulated total reaches or exceeds
    /// the interval, iterates every entity with <see cref="ShopComponent"/>, calls
    /// <see cref="IShopSystem.PlanRestock"/> to compute per-row shortfalls, then spawns exactly
    /// the shortfall count via <see cref="ITemplateRegistry.Spawn"/> and stamps each fresh entity
    /// with <see cref="ShopStockComponent"/>&#160;<c>{ Base }</c>. Publishes nothing — closed
    /// sweep with no game-rule fan-out (INV-10).
    /// </para>
    ///
    /// <para>Priority: <see cref="HandlerPriority.Domain"/> (20).</para>
    /// </summary>
    public sealed class ShopRestockTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly IShopSystem _shopSystem;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly TimeSpan _restockInterval;

        private TimeSpan _accumulated = TimeSpan.Zero;

        public int Priority => HandlerPriority.Domain;

        public ShopRestockTickHandler(
            EntityService entityService,
            IShopSystem shopSystem,
            ITemplateRegistry templateRegistry,
            IOptions<ShopOptions> options)
        {
            _entityService = entityService;
            _shopSystem = shopSystem;
            _templateRegistry = templateRegistry;
            _restockInterval = options.Value.RestockInterval;
        }

        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            _accumulated += @event.Elapsed;
            if (_accumulated < _restockInterval)
                return Task.CompletedTask;

            _accumulated = TimeSpan.Zero;

            // Snapshot shop ids before iterating — avoid mutation during enumeration.
            var shopIds = _entityService.GetAllComponents<ShopComponent>()
                .Select(p => p.EntityId)
                .ToList();

            foreach (var shopId in shopIds)
            {
                var shortfalls = _shopSystem.PlanRestock(shopId);
                if (shortfalls.Count == 0)
                    continue;

                // Resolve the shop's InventoryComponent once per shop.
                if (!_entityService.TryGet<InventoryComponent>(shopId, out var inventory))
                    continue;

                foreach (var (blueprintId, count) in shortfalls)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var entity = _templateRegistry.Spawn(blueprintId);
                        _entityService.AddComponent(entity.Id, new ShopStockComponent
                        {
                            Provenance = StockProvenance.Base,
                        });
                        inventory!.ItemEntityIds.Add(entity.Id);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
