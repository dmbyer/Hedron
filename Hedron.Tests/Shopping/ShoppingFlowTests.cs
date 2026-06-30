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
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Spawn.Handlers;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 3 flow tests for the shopping buy/sell/buy-back journeys (WP-2).
    ///
    /// Coverage contract (shopping.md Test plan — flow tier):
    ///   • End-to-end buy: player wallet decreases and till increases by exactly <c>buyPrice</c>;
    ///     item is in player inventory; <see cref="ItemBoughtEvent"/> published.
    ///   • End-to-end sell then buy (buy-back): item round-trips; player nets the buy/sell ratio
    ///     spread; persistence pool flips persistent → transient → persistent.
    /// </summary>
    public sealed class ShoppingFlowTests
    {
        // ── Fixtures ──────────────────────────────────────────────────────────────

        private static readonly ShopOptions DefaultOptions = new()
        {
            BuyRatio = 2.0m,
            SellRatio = 0.5m,
            BuyBackRetention = TimeSpan.FromHours(1),
            DefaultTillSeed = 100_000,
        };

        /// <summary>Builds the full system stack wired to a single EntityService.</summary>
        private static (
            ShopSystem shopSystem,
            ItemSystem itemSystem,
            WalletSystem walletSystem,
            ItemContextHandler contextHandler,
            RecordingEventBus bus,
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
            var contextHandler = new ItemContextHandler(ecs);

            // Bus with dispatch so handlers fire when events are published.
            var bus = new RecordingEventBus(dispatch: true);
            bus.Subscribe<ItemBoughtEvent>(contextHandler);
            bus.Subscribe<ItemSoldEvent>(contextHandler);

            return (shopSystem, itemSystem, walletSystem, contextHandler, bus, ecs, clock);
        }

        private static uint MakeShop(EntityService ecs, long tillBalance = 100_000)
        {
            var shop = ecs.CreateEntity();
            ecs.AddComponent(shop.Id, new ShopComponent { AcceptedCurrency = CurrencyId.Coin });
            ecs.AddComponent(shop.Id, new InventoryComponent());

            var wallet = new WalletComponent();
            wallet.Balances[CurrencyId.Coin] = tillBalance;
            ecs.AddComponent(shop.Id, wallet);

            ecs.AddComponent(shop.Id, new LocationComponent { RoomEntityId = 1u });
            return shop.Id;
        }

        private static uint MakePlayer(EntityService ecs, long coinBalance = 0)
        {
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new InventoryComponent());
            ecs.AddComponent(player.Id, new LocationComponent { RoomEntityId = 1u });

            if (coinBalance > 0)
            {
                var wallet = new WalletComponent();
                wallet.Balances[CurrencyId.Coin] = coinBalance;
                ecs.AddComponent(player.Id, wallet);
            }

            return player.Id;
        }

        private static uint MakeShopItem(EntityService ecs, uint shopEntityId, long value = 100,
            string name = "sword", string? blueprintId = null,
            StockProvenance provenance = StockProvenance.Base)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = name, Value = value });
            ecs.AddComponent(item.Id, new ShopStockComponent { Provenance = provenance });
            if (blueprintId != null)
                ecs.AddComponent(item.Id, new BlueprintComponent { BlueprintId = blueprintId });
            ecs.Get<InventoryComponent>(shopEntityId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        // ── End-to-end buy flow ───────────────────────────────────────────────────

        [Fact]
        public async Task Buy_player_wallet_decreases_by_buy_price()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100, name: "sword");

            // Simulate BuyCommand: validate → transfer → move → publish.
            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, itemId);
            Assert.True(buyResult.Success);

            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(itemId, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, buyResult.Price, buyResult.Currency));

            // Player paid buyPrice = 100 × 2.0 = 200.
            Assert.Equal(800L, walletSystem.GetBalance(playerId, CurrencyId.Coin)); // 1000 - 200
        }

        [Fact]
        public async Task Buy_till_increases_by_buy_price()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100, name: "sword");

            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, itemId);
            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(itemId, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, buyResult.Price, buyResult.Currency));

            Assert.Equal(100_200L, walletSystem.GetBalance(shopId, CurrencyId.Coin)); // 100000 + 200
        }

        [Fact]
        public async Task Buy_item_is_in_player_inventory()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100, name: "helm");

            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, itemId);
            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(itemId, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, buyResult.Price, buyResult.Currency));

            Assert.Contains(itemId, ecs.Get<InventoryComponent>(playerId).ItemEntityIds);
            Assert.DoesNotContain(itemId, ecs.Get<InventoryComponent>(shopId).ItemEntityIds);
        }

        [Fact]
        public async Task Buy_publishes_ItemBoughtEvent()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100, name: "bow");

            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, itemId);
            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(itemId, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, buyResult.Price, buyResult.Currency));

            var evt = bus.Published.OfType<ItemBoughtEvent>().Single();
            Assert.Equal(playerId, evt.PlayerEntityId);
            Assert.Equal(shopId, evt.ShopEntityId);
            Assert.Equal(itemId, evt.ItemEntityId);
            Assert.Equal(200L, evt.PricePaid);
        }

        [Fact]
        public async Task Buy_ItemContextHandler_adds_PersistentEntity_to_item()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100, name: "shield");

            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, itemId);
            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(itemId, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, itemId, 1u, buyResult.Price, buyResult.Currency));

            Assert.True(ecs.HasComponent<PersistentEntity>(itemId),
                "ItemContextHandler must add PersistentEntity on buy.");
        }

        // ── End-to-end sell flow ──────────────────────────────────────────────────

        [Fact]
        public async Task Sell_player_wallet_increases_by_sell_price()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, clock) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 0);

            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "ring", Value = 200 });
            ecs.AddComponent(item, new PersistentEntity()); // player-owned
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var sellResult = shopSystem.TryResolveSell(playerId, shopId, item);
            Assert.True(sellResult.Success);

            walletSystem.Transfer(shopId, playerId, sellResult.Currency, sellResult.Price);
            itemSystem.MoveBetweenInventories(item, playerId, shopId);
            ecs.AddComponent(item, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = sellResult.ExpiresAt,
            });
            await bus.PublishAsync(new ItemSoldEvent(playerId, shopId, item, 1u, sellResult.Price, sellResult.Currency));

            Assert.Equal(100L, walletSystem.GetBalance(playerId, CurrencyId.Coin)); // 200 × 0.5
        }

        [Fact]
        public async Task Sell_till_decreases_by_sell_price()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 0);

            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "gem", Value = 200 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var sellResult = shopSystem.TryResolveSell(playerId, shopId, item);
            walletSystem.Transfer(shopId, playerId, sellResult.Currency, sellResult.Price);
            itemSystem.MoveBetweenInventories(item, playerId, shopId);
            ecs.AddComponent(item, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = sellResult.ExpiresAt,
            });
            await bus.PublishAsync(new ItemSoldEvent(playerId, shopId, item, 1u, sellResult.Price, sellResult.Currency));

            Assert.Equal(99_900L, walletSystem.GetBalance(shopId, CurrencyId.Coin)); // 100000 - 100
        }

        // ── Sell → Buy-back full round-trip ──────────────────────────────────────

        [Fact]
        public async Task SellThenBuyback_item_roundtrips_and_player_nets_spread()
        {
            // Buy ratio = 2×, sell ratio = 0.5×, item value = 100.
            // Player buys at 200 (not tested here), sells at 50, buys back at 50 → net = -200 overall.
            // This test verifies the sell+buyback sub-journey: player nets 50 (sell) – 50 (buyback) = 0.
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, clock) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 200);

            // Setup: item is in player inventory (simulates having bought it).
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "orb", Value = 100 });
            ecs.AddComponent(item, new BlueprintComponent { BlueprintId = "item.orb.01" });
            ecs.AddComponent(item, new PersistentEntity());
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            // ── Sell ──
            var sellResult = shopSystem.TryResolveSell(playerId, shopId, item);
            Assert.True(sellResult.Success);
            Assert.Equal(50L, sellResult.Price);

            walletSystem.Transfer(shopId, playerId, sellResult.Currency, sellResult.Price);
            itemSystem.MoveBetweenInventories(item, playerId, shopId);
            ecs.AddComponent(item, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = sellResult.ExpiresAt,
            });
            await bus.PublishAsync(new ItemSoldEvent(playerId, shopId, item, 1u, sellResult.Price, sellResult.Currency));

            Assert.False(ecs.HasComponent<PersistentEntity>(item), "After sell: world-transient.");
            Assert.Equal(250L, walletSystem.GetBalance(playerId, CurrencyId.Coin)); // 200 + 50
            Assert.DoesNotContain(item, ecs.Get<InventoryComponent>(playerId).ItemEntityIds);
            Assert.Contains(item, ecs.Get<InventoryComponent>(shopId).ItemEntityIds);

            // ── Buy-back ──
            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, item);
            Assert.True(buyResult.Success);
            Assert.Equal(50L, buyResult.Price); // buy-back = sell price

            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(item, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, item, 1u, buyResult.Price, buyResult.Currency));

            Assert.True(ecs.HasComponent<PersistentEntity>(item), "After buy-back: persistent again.");
            Assert.Equal(200L, walletSystem.GetBalance(playerId, CurrencyId.Coin)); // 250 - 50
            Assert.Contains(item, ecs.Get<InventoryComponent>(playerId).ItemEntityIds);
            Assert.DoesNotContain(item, ecs.Get<InventoryComponent>(shopId).ItemEntityIds);
            Assert.False(ecs.HasComponent<ShopStockComponent>(item), "ShopStockComponent cleared after buy-back.");
            Assert.True(ecs.HasComponent<BlueprintComponent>(item), "INV-21: BlueprintComponent preserved throughout.");
        }

        [Fact]
        public async Task SellThenBuyback_persistence_pool_flips_persistent_transient_persistent()
        {
            var (shopSystem, itemSystem, walletSystem, _, bus, ecs, clock) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 1_000);

            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "amulet", Value = 100 });
            ecs.AddComponent(item, new PersistentEntity());
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            // Sell → transient
            var sellResult = shopSystem.TryResolveSell(playerId, shopId, item);
            walletSystem.Transfer(shopId, playerId, sellResult.Currency, sellResult.Price);
            itemSystem.MoveBetweenInventories(item, playerId, shopId);
            ecs.AddComponent(item, new ShopStockComponent { Provenance = StockProvenance.Acquired, ExpiresAt = sellResult.ExpiresAt });
            await bus.PublishAsync(new ItemSoldEvent(playerId, shopId, item, 1u, sellResult.Price, sellResult.Currency));

            Assert.False(ecs.HasComponent<PersistentEntity>(item)); // transient

            // Buy-back → persistent
            var buyResult = shopSystem.TryResolveBuy(playerId, shopId, item);
            walletSystem.Transfer(playerId, shopId, buyResult.Currency, buyResult.Price);
            itemSystem.MoveBetweenInventories(item, shopId, playerId);
            await bus.PublishAsync(new ItemBoughtEvent(playerId, shopId, item, 1u, buyResult.Price, buyResult.Currency));

            Assert.True(ecs.HasComponent<PersistentEntity>(item)); // persistent again
        }
    }
}
