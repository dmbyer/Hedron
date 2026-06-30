using System;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 1 system-unit tests for <see cref="ShopSystem"/> / <see cref="IShopSystem"/>.
    ///
    /// Coverage contract (shopping.md Test plan — system-unit):
    ///   • <c>TryResolveBuy</c> prices base stock at <c>Value × BuyRatio</c>;
    ///     affordable vs. unaffordable returns success/refusal without mutating.
    ///   • <c>TryResolveBuy</c> against an <c>Acquired</c> item prices at <c>Value × SellRatio</c>
    ///     (buy-back price = what the shop paid).
    ///   • <c>TryResolveSell</c> prices at <c>Value × SellRatio</c>; refuses <c>Value == 0</c>;
    ///     refuses when till cannot afford.
    ///   • <c>PlanRestock</c> returns authored − liveBaseCount per row; returns zero shortfall when
    ///     full; ignores <c>Acquired</c> items in the count (top-up semantics).
    ///   • <c>FindExpired</c> returns exactly <c>Acquired</c> items with
    ///     <c>ExpiresAt &lt;= nowUtc</c>; never returns a <c>Base</c> item.
    ///   • <c>TryResolveSell</c> returns the clock-derived <c>ExpiresAt = now + retention</c>.
    /// </summary>
    public sealed class ShopSystemTests
    {
        // ── Fixtures ─────────────────────────────────────────────────────────────

        private static readonly ShopOptions DefaultOptions = new()
        {
            BuyRatio = 2.0m,
            SellRatio = 0.5m,
            BuyBackRetention = TimeSpan.FromHours(1),
            DefaultTillSeed = 100_000,
        };

        private static (ShopSystem system, EntityService ecs, FakeClock clock, WalletSystem wallet) Build(
            ShopOptions? options = null)
        {
            var ecs = new EntityService();
            var clock = new FakeClock();
            var walletSystem = new WalletSystem(ecs);
            var itemSystem = new ItemSystem(ecs);
            var system = new ShopSystem(
                ecs,
                walletSystem,
                itemSystem,
                clock,
                Options.Create(options ?? DefaultOptions));
            return (system, ecs, clock, walletSystem);
        }

        /// <summary>Creates a shopkeeper entity with ShopComponent, WalletComponent, InventoryComponent.</summary>
        private static uint MakeShop(EntityService ecs, long tillBalance = 100_000)
        {
            var shop = ecs.CreateEntity();
            ecs.AddComponent(shop.Id, new ShopComponent { AcceptedCurrency = CurrencyId.Coin });
            ecs.AddComponent(shop.Id, new InventoryComponent());

            if (tillBalance > 0)
            {
                var wallet = new WalletComponent();
                wallet.Balances[CurrencyId.Coin] = tillBalance;
                ecs.AddComponent(shop.Id, wallet);
            }

            ecs.AddComponent(shop.Id, new LocationComponent { RoomEntityId = 1u });
            return shop.Id;
        }

        /// <summary>Creates a player entity with an InventoryComponent and a wallet.</summary>
        private static uint MakePlayer(EntityService ecs, long coinBalance = 0)
        {
            var player = ecs.CreateEntity();
            ecs.AddComponent(player.Id, new PlayerComponent { DisplayName = "Tester" });
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

        /// <summary>Creates a shop item in the shopkeeper's inventory with the given Value.</summary>
        private static uint MakeShopItem(EntityService ecs, uint shopEntityId, long value = 100,
            string name = "sword", string? blueprintId = null,
            StockProvenance provenance = StockProvenance.Base, DateTime? expiresAt = null)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = name, Value = value });
            ecs.AddComponent(item.Id, new ShopStockComponent { Provenance = provenance, ExpiresAt = expiresAt });
            if (blueprintId != null)
                ecs.AddComponent(item.Id, new BlueprintComponent { BlueprintId = blueprintId });
            ecs.Get<InventoryComponent>(shopEntityId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        // ── TryResolveBuy — base stock ────────────────────────────────────────────

        [Fact]
        public void TryResolveBuy_prices_base_stock_at_Value_times_BuyRatio()
        {
            var (sys, ecs, _, wallet) = Build();
            var shopId = MakeShop(ecs);
            var playerId = MakePlayer(ecs, coinBalance: 10_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100);

            var result = sys.TryResolveBuy(playerId, shopId, itemId);

            Assert.True(result.Success);
            Assert.Equal(200L, result.Price); // 100 × 2.0
            Assert.Equal(CurrencyId.Coin, result.Currency);
        }

        [Fact]
        public void TryResolveBuy_returns_failure_when_player_cannot_afford()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            var playerId = MakePlayer(ecs, coinBalance: 50); // needs 200
            var itemId = MakeShopItem(ecs, shopId, value: 100);

            var result = sys.TryResolveBuy(playerId, shopId, itemId);

            Assert.False(result.Success);
            Assert.Equal(200L, result.Price);
            Assert.NotNull(result.FailureReason);
        }

        [Fact]
        public void TryResolveBuy_does_not_mutate_wallets()
        {
            var (sys, ecs, _, wallet) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 10_000);
            var itemId = MakeShopItem(ecs, shopId, value: 100);

            sys.TryResolveBuy(playerId, shopId, itemId);

            Assert.Equal(10_000L, wallet.GetBalance(playerId, CurrencyId.Coin));
            Assert.Equal(100_000L, wallet.GetBalance(shopId, CurrencyId.Coin));
        }

        // ── TryResolveBuy — buy-back (Acquired) ───────────────────────────────────

        [Fact]
        public void TryResolveBuy_prices_Acquired_item_at_SellRatio_buyback_price()
        {
            // Buy-back price = SellRatio × Value (what the shop paid the player — resolved decision 5).
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            var playerId = MakePlayer(ecs, coinBalance: 10_000);

            var itemId = MakeShopItem(ecs, shopId, value: 100, provenance: StockProvenance.Acquired);

            var result = sys.TryResolveBuy(playerId, shopId, itemId);

            Assert.True(result.Success);
            Assert.Equal(50L, result.Price); // 100 × 0.5 = 50 (buy-back price)
        }

        [Fact]
        public void TryResolveBuy_Acquired_unaffordable_returns_failure()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            var playerId = MakePlayer(ecs, coinBalance: 10); // needs 50
            var itemId = MakeShopItem(ecs, shopId, value: 100, provenance: StockProvenance.Acquired);

            var result = sys.TryResolveBuy(playerId, shopId, itemId);

            Assert.False(result.Success);
            Assert.Equal(50L, result.Price);
        }

        // ── TryResolveSell ────────────────────────────────────────────────────────

        [Fact]
        public void TryResolveSell_prices_item_at_Value_times_SellRatio()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs);
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "dagger", Value = 80 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var result = sys.TryResolveSell(playerId, shopId, item);

            Assert.True(result.Success);
            Assert.Equal(40L, result.Price); // 80 × 0.5
        }

        [Fact]
        public void TryResolveSell_refuses_item_with_Value_zero()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            var playerId = MakePlayer(ecs);
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "junk", Value = 0 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var result = sys.TryResolveSell(playerId, shopId, item);

            Assert.False(result.Success);
            Assert.NotNull(result.FailureReason);
        }

        [Fact]
        public void TryResolveSell_refuses_when_till_cannot_afford()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 5); // needs to pay 50
            var playerId = MakePlayer(ecs);
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "ring", Value = 100 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var result = sys.TryResolveSell(playerId, shopId, item);

            Assert.False(result.Success);
            Assert.NotNull(result.FailureReason);
        }

        [Fact]
        public void TryResolveSell_returns_clock_derived_ExpiresAt()
        {
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs);
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "ring", Value = 100 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            var before = clock.UtcNow;
            var result = sys.TryResolveSell(playerId, shopId, item);

            Assert.True(result.Success);
            Assert.NotNull(result.ExpiresAt);
            // ExpiresAt = now + BuyBackRetention (1 hour by default)
            Assert.Equal(before + DefaultOptions.BuyBackRetention, result.ExpiresAt!.Value);
        }

        [Fact]
        public void TryResolveSell_does_not_mutate_wallets()
        {
            var (sys, ecs, _, wallet) = Build();
            var shopId = MakeShop(ecs, tillBalance: 100_000);
            var playerId = MakePlayer(ecs, coinBalance: 0);
            var item = ecs.CreateEntity().Id;
            ecs.AddComponent(item, new ItemDataComponent { Name = "helm", Value = 200 });
            ecs.Get<InventoryComponent>(playerId).ItemEntityIds.Add(item);

            sys.TryResolveSell(playerId, shopId, item);

            Assert.Equal(100_000L, wallet.GetBalance(shopId, CurrencyId.Coin));
            Assert.Equal(0L, wallet.GetBalance(playerId, CurrencyId.Coin));
        }

        // ── PlanRestock ───────────────────────────────────────────────────────────

        [Fact]
        public void PlanRestock_returns_shortfall_for_undersupplied_row()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            ecs.Get<ShopComponent>(shopId).BaseStock.Add(new ShopStockRow
            {
                BlueprintId = "item.sword",
                Quantity = 3,
            });

            // Only 1 live Base entity.
            MakeShopItem(ecs, shopId, blueprintId: "item.sword");

            var shortfalls = sys.PlanRestock(shopId);

            Assert.Single(shortfalls);
            Assert.Equal("item.sword", shortfalls[0].BlueprintId);
            Assert.Equal(2, shortfalls[0].Shortfall);
        }

        [Fact]
        public void PlanRestock_returns_empty_when_fully_stocked()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            ecs.Get<ShopComponent>(shopId).BaseStock.Add(new ShopStockRow
            {
                BlueprintId = "item.sword",
                Quantity = 2,
            });

            MakeShopItem(ecs, shopId, blueprintId: "item.sword");
            MakeShopItem(ecs, shopId, blueprintId: "item.sword");

            var shortfalls = sys.PlanRestock(shopId);

            Assert.Empty(shortfalls);
        }

        [Fact]
        public void PlanRestock_ignores_Acquired_items_in_live_count()
        {
            // Acquired items should not count toward the base-stock level (top-up semantics, Q1).
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            ecs.Get<ShopComponent>(shopId).BaseStock.Add(new ShopStockRow
            {
                BlueprintId = "item.dagger",
                Quantity = 2,
            });

            // One Acquired item with the same blueprint (sold back by a player).
            MakeShopItem(ecs, shopId, blueprintId: "item.dagger", provenance: StockProvenance.Acquired);

            var shortfalls = sys.PlanRestock(shopId);

            // Shortfall = 2 (authored) - 0 (Base) = 2; Acquired item was ignored.
            Assert.Single(shortfalls);
            Assert.Equal(2, shortfalls[0].Shortfall);
        }

        [Fact]
        public void PlanRestock_handles_multiple_rows()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            ecs.Get<ShopComponent>(shopId).BaseStock.Add(new ShopStockRow { BlueprintId = "item.sword", Quantity = 2 });
            ecs.Get<ShopComponent>(shopId).BaseStock.Add(new ShopStockRow { BlueprintId = "item.potion", Quantity = 1 });

            // Sword: 1 live base entity → shortfall = 1. Potion: 1 live → shortfall = 0.
            MakeShopItem(ecs, shopId, blueprintId: "item.sword");
            MakeShopItem(ecs, shopId, blueprintId: "item.potion");

            var shortfalls = sys.PlanRestock(shopId);

            Assert.Single(shortfalls); // only the sword row has a shortfall
            Assert.Equal("item.sword", shortfalls[0].BlueprintId);
            Assert.Equal(1, shortfalls[0].Shortfall);
        }

        // ── FindExpired ───────────────────────────────────────────────────────────

        [Fact]
        public void FindExpired_returns_Acquired_items_whose_ExpiresAt_is_past()
        {
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs);
            var past = clock.UtcNow - TimeSpan.FromMinutes(1);

            var expiredItem = MakeShopItem(ecs, shopId, provenance: StockProvenance.Acquired, expiresAt: past);

            var expired = sys.FindExpired(shopId, clock.UtcNow);

            Assert.Contains(expiredItem, expired);
        }

        [Fact]
        public void FindExpired_does_not_return_Acquired_item_not_yet_expired()
        {
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs);
            var future = clock.UtcNow + TimeSpan.FromHours(1);

            var freshItem = MakeShopItem(ecs, shopId, provenance: StockProvenance.Acquired, expiresAt: future);

            var expired = sys.FindExpired(shopId, clock.UtcNow);

            Assert.DoesNotContain(freshItem, expired);
        }

        [Fact]
        public void FindExpired_never_returns_Base_items()
        {
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs);
            // Base item has no ExpiresAt but we add one to be safe.
            var baseItem = MakeShopItem(ecs, shopId, provenance: StockProvenance.Base, expiresAt: clock.UtcNow - TimeSpan.FromSeconds(1));

            var expired = sys.FindExpired(shopId, clock.UtcNow);

            Assert.DoesNotContain(baseItem, expired);
        }

        [Fact]
        public void FindExpired_returns_exact_boundary_at_nowUtc()
        {
            // ExpiresAt == nowUtc should be considered expired (<= semantics).
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs);
            var exactNow = clock.UtcNow;
            var boundaryItem = MakeShopItem(ecs, shopId, provenance: StockProvenance.Acquired, expiresAt: exactNow);

            var expired = sys.FindExpired(shopId, exactNow);

            Assert.Contains(boundaryItem, expired);
        }

        [Fact]
        public void FindExpired_returns_empty_for_shop_with_no_items()
        {
            var (sys, ecs, clock, _) = Build();
            var shopId = MakeShop(ecs);

            var expired = sys.FindExpired(shopId, clock.UtcNow);

            Assert.Empty(expired);
        }

        // ── GetListing ────────────────────────────────────────────────────────────

        [Fact]
        public void GetListing_returns_all_items_in_shop_inventory()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            MakeShopItem(ecs, shopId, value: 100, name: "sword");
            MakeShopItem(ecs, shopId, value: 50, name: "potion", provenance: StockProvenance.Acquired);

            var listing = sys.GetListing(shopId);

            Assert.Equal(2, listing.Rows.Count);
        }

        [Fact]
        public void GetListing_acquired_row_has_IsAcquired_true()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            MakeShopItem(ecs, shopId, value: 50, name: "helm", provenance: StockProvenance.Acquired);

            var listing = sys.GetListing(shopId);

            Assert.True(listing.Rows.Single().IsAcquired);
        }

        [Fact]
        public void GetListing_base_row_has_BuyPrice_at_BuyRatio()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            MakeShopItem(ecs, shopId, value: 100, name: "sword");

            var listing = sys.GetListing(shopId);

            Assert.Equal(200L, listing.Rows.Single().BuyPrice);
        }

        [Fact]
        public void GetListing_acquired_row_has_BuyPrice_at_SellRatio_buyback()
        {
            var (sys, ecs, _, _) = Build();
            var shopId = MakeShop(ecs);
            MakeShopItem(ecs, shopId, value: 100, name: "ring", provenance: StockProvenance.Acquired);

            var listing = sys.GetListing(shopId);

            Assert.Equal(50L, listing.Rows.Single().BuyPrice); // buy-back = SellRatio × Value
        }
    }
}
