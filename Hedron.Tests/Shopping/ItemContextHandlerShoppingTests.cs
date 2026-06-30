using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Modules.Spawn.Handlers;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 1 handler tests for the shopping extensions to <see cref="ItemContextHandler"/>.
    ///
    /// Coverage contract (shopping.md Test plan — handler tier):
    ///   • <see cref="ItemBoughtEvent"/> → item gains <c>PersistentEntity</c>, <b>retains</b>
    ///     <c>BlueprintComponent</c> (INV-21), and <c>ShopStockComponent</c> is removed.
    ///   • <see cref="ItemSoldEvent"/> → item loses <c>PersistentEntity</c>.
    /// </summary>
    public sealed class ItemContextHandlerShoppingTests
    {
        // ── Fixtures ──────────────────────────────────────────────────────────────

        private static (ItemContextHandler handler, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            return (new ItemContextHandler(ecs), ecs);
        }

        private static ItemBoughtEvent MakeBoughtEvent(EntityService ecs,
            uint? playerId = null, uint? shopId = null, uint? itemId = null)
        {
            return new ItemBoughtEvent(
                playerId ?? ecs.CreateEntity().Id,
                shopId ?? ecs.CreateEntity().Id,
                itemId ?? ecs.CreateEntity().Id,
                1u,
                200L,
                CurrencyId.Coin);
        }

        private static ItemSoldEvent MakeSoldEvent(EntityService ecs,
            uint? playerId = null, uint? shopId = null, uint? itemId = null)
        {
            return new ItemSoldEvent(
                playerId ?? ecs.CreateEntity().Id,
                shopId ?? ecs.CreateEntity().Id,
                itemId ?? ecs.CreateEntity().Id,
                1u,
                50L,
                CurrencyId.Coin);
        }

        // ── ItemBoughtEvent: PersistentEntity ─────────────────────────────────────

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_adds_PersistentEntity()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "sword" });

            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.True(ecs.HasComponent<PersistentEntity>(itemId),
                "Bought item must gain PersistentEntity (player-owned context).");
        }

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_does_not_add_duplicate_PersistentEntity()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "sword" });
            ecs.AddComponent(itemId, new PersistentEntity()); // already persistent

            // Should not throw (EntityService.AddComponent would throw on duplicate).
            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.True(ecs.HasComponent<PersistentEntity>(itemId));
        }

        // ── ItemBoughtEvent: BlueprintComponent preservation (INV-21) ─────────────

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_does_NOT_remove_BlueprintComponent_INV21()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "ring" });
            ecs.AddComponent(itemId, new BlueprintComponent { BlueprintId = "item.ring.01" });

            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.True(ecs.HasComponent<BlueprintComponent>(itemId),
                "INV-21: BlueprintComponent must be preserved on buy as an origin record.");
            Assert.Equal("item.ring.01", ecs.Get<BlueprintComponent>(itemId).BlueprintId);
        }

        // ── ItemBoughtEvent: ShopStockComponent removal ───────────────────────────

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_removes_ShopStockComponent()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "potion" });
            ecs.AddComponent(itemId, new ShopStockComponent { Provenance = StockProvenance.Base });

            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.False(ecs.HasComponent<ShopStockComponent>(itemId),
                "ShopStockComponent must be removed when a base-stock item is bought.");
        }

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_removes_ShopStockComponent_for_Acquired_item()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "gem" });
            ecs.AddComponent(itemId, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = System.DateTime.UtcNow.AddHours(1),
            });

            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.False(ecs.HasComponent<ShopStockComponent>(itemId),
                "ShopStockComponent must be removed when a buy-back item is bought.");
        }

        [Fact]
        public async Task HandleAsync_ItemBoughtEvent_noop_when_item_has_no_ShopStockComponent()
        {
            // Edge: item with no ShopStockComponent (e.g. future resale of a crafted item).
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "crafted dagger" });

            // Should not throw when there is no ShopStockComponent to remove.
            await handler.HandleAsync(MakeBoughtEvent(ecs, itemId: itemId));

            Assert.True(ecs.HasComponent<PersistentEntity>(itemId));
        }

        // ── ItemSoldEvent: PersistentEntity removal ───────────────────────────────

        [Fact]
        public async Task HandleAsync_ItemSoldEvent_removes_PersistentEntity()
        {
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "shield" });
            ecs.AddComponent(itemId, new PersistentEntity()); // player-owned before sale

            await handler.HandleAsync(MakeSoldEvent(ecs, itemId: itemId));

            Assert.False(ecs.HasComponent<PersistentEntity>(itemId),
                "Sold item must lose PersistentEntity (becomes world-transient on buy-back shelf).");
        }

        [Fact]
        public async Task HandleAsync_ItemSoldEvent_noop_when_item_has_no_PersistentEntity()
        {
            // Should not throw even if the item was never persistent.
            var (handler, ecs) = Build();
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "orphan" });

            await handler.HandleAsync(MakeSoldEvent(ecs, itemId: itemId));

            Assert.False(ecs.HasComponent<PersistentEntity>(itemId));
        }

        // ── Round-trip: buy → sell → buy-back persistence pool ────────────────────

        [Fact]
        public async Task BuySellBuyback_persistence_pool_flips_correctly()
        {
            var (handler, ecs) = Build();
            var playerId = ecs.CreateEntity().Id;
            var shopId = ecs.CreateEntity().Id;
            var itemId = ecs.CreateEntity().Id;
            ecs.AddComponent(itemId, new ItemDataComponent { Name = "amulet" });
            ecs.AddComponent(itemId, new BlueprintComponent { BlueprintId = "item.amulet.01" });
            ecs.AddComponent(itemId, new ShopStockComponent { Provenance = StockProvenance.Base });

            // 1. Player buys item from shop → persistent.
            await handler.HandleAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, 200L, CurrencyId.Coin));
            Assert.True(ecs.HasComponent<PersistentEntity>(itemId));
            Assert.False(ecs.HasComponent<ShopStockComponent>(itemId));
            Assert.True(ecs.HasComponent<BlueprintComponent>(itemId), "INV-21: BlueprintComponent preserved.");

            // 2. Player sells it back → world-transient.
            await handler.HandleAsync(new ItemSoldEvent(playerId, shopId, itemId, 1u, 50L, CurrencyId.Coin));
            Assert.False(ecs.HasComponent<PersistentEntity>(itemId));

            // Re-stamp ShopStockComponent as the SellCommand does (Acquired).
            ecs.AddComponent(itemId, new ShopStockComponent { Provenance = StockProvenance.Acquired });

            // 3. Player buys it back again → persistent again.
            await handler.HandleAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, 50L, CurrencyId.Coin));
            Assert.True(ecs.HasComponent<PersistentEntity>(itemId));
            Assert.False(ecs.HasComponent<ShopStockComponent>(itemId));
            Assert.True(ecs.HasComponent<BlueprintComponent>(itemId), "INV-21: BlueprintComponent preserved after buy-back.");
        }
    }
}
