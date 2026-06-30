using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Handlers;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 2 handler tests for <see cref="ShopExpiryTickHandler"/>.
    ///
    /// Coverage contract (shopping.md Test plan — handler tier):
    ///   • Forced tick at interval destroys past-<see cref="ShopStockComponent.ExpiresAt"/> acquired items only.
    ///   • Not-yet-expired acquired items survive.
    ///   • Base-stock items are never destroyed, even when they carry an ExpiresAt past the clock.
    ///   • Sub-interval tick is a no-op — no entities destroyed.
    ///   • Accumulated sub-interval ticks trigger at the interval boundary.
    /// </summary>
    public sealed class ShopExpiryTickHandlerTests
    {
        // ── Fixture ───────────────────────────────────────────────────────────────

        private static readonly ShopOptions DefaultOptions = new()
        {
            RestockInterval = TimeSpan.FromMinutes(5),
            BuyBackRetention = TimeSpan.FromHours(1),
            BuyRatio = 2.0m,
            SellRatio = 0.5m,
            DefaultTillSeed = 100_000,
        };

        private static (
            ShopExpiryTickHandler handler,
            EntityService ecs,
            FakeClock clock)
            Build(ShopOptions? options = null)
        {
            var ecs = new EntityService();
            var clock = new FakeClock();
            var opt = options ?? DefaultOptions;
            var walletSystem = new WalletSystem(ecs);
            var itemSystem = new ItemSystem(ecs);
            var shopSystem = new ShopSystem(ecs, walletSystem, itemSystem, clock, Options.Create(opt));

            var handler = new ShopExpiryTickHandler(ecs, shopSystem, clock, Options.Create(opt));
            return (handler, ecs, clock);
        }

        /// <summary>Creates a shopkeeper entity with ShopComponent and InventoryComponent.</summary>
        private static uint MakeShop(EntityService ecs)
        {
            var shop = ecs.CreateEntity();
            ecs.AddComponent(shop.Id, new ShopComponent { AcceptedCurrency = CurrencyId.Coin });
            ecs.AddComponent(shop.Id, new InventoryComponent());
            return shop.Id;
        }

        /// <summary>Adds an acquired item with the given ExpiresAt to the shop's inventory.</summary>
        private static uint AddAcquiredItem(EntityService ecs, uint shopId, DateTime expiresAt)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "sold-item", Value = 50 });
            ecs.AddComponent(item.Id, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = expiresAt,
            });
            ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        /// <summary>Adds a base-stock item (no ExpiresAt) to the shop's inventory.</summary>
        private static uint AddBaseItem(EntityService ecs, uint shopId)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "base-item", Value = 100 });
            ecs.AddComponent(item.Id, new ShopStockComponent { Provenance = StockProvenance.Base });
            ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        /// <summary>Builds a HeartbeatTickEvent whose Elapsed equals <paramref name="elapsed"/>.</summary>
        private static HeartbeatTickEvent Tick(long id, TimeSpan elapsed)
            => new HeartbeatTickEvent(TickId: id, Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(id), Elapsed: elapsed);

        // ── Tests ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task At_interval_destroys_expired_acquired_items()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            // Expired item: ExpiresAt is in the past relative to clock.UtcNow.
            var past = clock.UtcNow - TimeSpan.FromMinutes(1);
            var expiredItemId = AddAcquiredItem(ecs, shopId, expiresAt: past);

            await handler.HandleAsync(Tick(1, DefaultOptions.BuyBackRetention));

            // The expired item's entity should be destroyed.
            // EntityService.DestroyEntity removes all components — HasComponent returns false.
            Assert.False(ecs.HasComponent<ShopStockComponent>(expiredItemId),
                "Expired acquired item should have been destroyed.");
        }

        [Fact]
        public async Task At_interval_does_not_destroy_not_yet_expired_acquired_items()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            // Not-yet-expired item: ExpiresAt is in the future.
            var future = clock.UtcNow + TimeSpan.FromHours(1);
            var freshItemId = AddAcquiredItem(ecs, shopId, expiresAt: future);

            await handler.HandleAsync(Tick(1, DefaultOptions.BuyBackRetention));

            // The fresh item should survive.
            Assert.True(ecs.HasComponent<ShopStockComponent>(freshItemId),
                "Not-yet-expired acquired item must survive.");
        }

        [Fact]
        public async Task At_interval_never_destroys_base_items()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            var baseItemId = AddBaseItem(ecs, shopId);

            await handler.HandleAsync(Tick(1, DefaultOptions.BuyBackRetention));

            // Base-stock items must never be touched by the expiry sweep.
            Assert.True(ecs.HasComponent<ShopStockComponent>(baseItemId),
                "Base-stock item must never be destroyed by the expiry sweep.");
            Assert.Equal(StockProvenance.Base, ecs.Get<ShopStockComponent>(baseItemId).Provenance);
        }

        [Fact]
        public async Task At_interval_only_destroys_expired_leaving_fresh_untouched()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            var past = clock.UtcNow - TimeSpan.FromSeconds(1);
            var future = clock.UtcNow + TimeSpan.FromHours(2);

            var expiredId = AddAcquiredItem(ecs, shopId, expiresAt: past);
            var freshId = AddAcquiredItem(ecs, shopId, expiresAt: future);
            var baseId = AddBaseItem(ecs, shopId);

            await handler.HandleAsync(Tick(1, DefaultOptions.BuyBackRetention));

            Assert.False(ecs.HasComponent<ShopStockComponent>(expiredId), "Expired item destroyed.");
            Assert.True(ecs.HasComponent<ShopStockComponent>(freshId), "Fresh acquired item survives.");
            Assert.True(ecs.HasComponent<ShopStockComponent>(baseId), "Base item survives.");
        }

        [Fact]
        public async Task Sub_interval_tick_is_a_no_op()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            // Expired item — should NOT be destroyed before the interval fires.
            var past = clock.UtcNow - TimeSpan.FromMinutes(1);
            var expiredItemId = AddAcquiredItem(ecs, shopId, expiresAt: past);

            // Fire a tick less than the full interval.
            var subInterval = TimeSpan.FromMilliseconds(DefaultOptions.BuyBackRetention.TotalMilliseconds / 2);
            await handler.HandleAsync(Tick(1, subInterval));

            // Entity should still exist — interval has not elapsed yet.
            Assert.True(ecs.HasComponent<ShopStockComponent>(expiredItemId),
                "Expiry sweep must not run before the interval elapses.");
        }

        [Fact]
        public async Task Accumulated_sub_interval_ticks_trigger_at_boundary()
        {
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            var past = clock.UtcNow - TimeSpan.FromMinutes(1);
            var expiredItemId = AddAcquiredItem(ecs, shopId, expiresAt: past);

            var halfInterval = TimeSpan.FromMilliseconds(DefaultOptions.BuyBackRetention.TotalMilliseconds / 2);

            await handler.HandleAsync(Tick(1, halfInterval));
            Assert.True(ecs.HasComponent<ShopStockComponent>(expiredItemId), "Not yet triggered.");

            await handler.HandleAsync(Tick(2, halfInterval));
            Assert.False(ecs.HasComponent<ShopStockComponent>(expiredItemId), "Triggered at boundary.");
        }

        [Fact]
        public async Task Boundary_exact_ExpiresAt_equals_nowUtc_is_expired()
        {
            // ExpiresAt == nowUtc → expired (<= semantics, matching IShopSystem.FindExpired).
            var (handler, ecs, clock) = Build();
            var shopId = MakeShop(ecs);

            var exactNow = clock.UtcNow;
            var boundaryItemId = AddAcquiredItem(ecs, shopId, expiresAt: exactNow);

            await handler.HandleAsync(Tick(1, DefaultOptions.BuyBackRetention));

            Assert.False(ecs.HasComponent<ShopStockComponent>(boundaryItemId),
                "Item with ExpiresAt == nowUtc should be destroyed (<= semantics).");
        }
    }
}
