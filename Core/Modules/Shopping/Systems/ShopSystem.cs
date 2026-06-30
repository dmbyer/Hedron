using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Shopping.Systems
{
    /// <summary>
    /// Pure domain system implementing <see cref="IShopSystem"/>. Composes
    /// <see cref="IWalletSystem"/> and <see cref="IItemSystem"/> for affordability/inventory
    /// checks; uses <see cref="IClock"/> for all time decisions (INV-26). Never publishes events
    /// or calls persistence (INV-5).
    /// </summary>
    public sealed class ShopSystem : IShopSystem
    {
        private readonly EntityService _ecs;
        private readonly IWalletSystem _walletSystem;
        private readonly IItemSystem _itemSystem;
        private readonly IClock _clock;
        private readonly ShopOptions _options;

        public ShopSystem(
            EntityService ecs,
            IWalletSystem walletSystem,
            IItemSystem itemSystem,
            IClock clock,
            IOptions<ShopOptions> options)
        {
            _ecs = ecs;
            _walletSystem = walletSystem;
            _itemSystem = itemSystem;
            _clock = clock;
            _options = options.Value;
        }

        // ── GetListing ────────────────────────────────────────────────────────────

        public ShopListing GetListing(uint shopEntityId)
        {
            if (!_ecs.TryGet<ShopComponent>(shopEntityId, out var shop))
                return new ShopListing(shopEntityId, CurrencyId.Coin, Array.Empty<ShopListingRow>());

            var currency = shop!.AcceptedCurrency;
            var rows = new List<ShopListingRow>();

            foreach (var itemId in _itemSystem.GetItemsInInventory(shopEntityId))
            {
                if (!_ecs.TryGet<ItemDataComponent>(itemId, out var data)) continue;

                var isAcquired = _ecs.TryGet<ShopStockComponent>(itemId, out var stock)
                    && stock!.Provenance == StockProvenance.Acquired;

                // Buy-back items are priced at SellRatio (what the shop paid); base items at BuyRatio.
                var price = isAcquired
                    ? (long)Math.Ceiling(data!.Value * _options.SellRatio)
                    : (long)Math.Ceiling(data!.Value * _options.BuyRatio);

                rows.Add(new ShopListingRow(itemId, data!.Name, price, currency, isAcquired));
            }

            return new ShopListing(shopEntityId, currency, rows);
        }

        // ── TryResolveBuy ─────────────────────────────────────────────────────────

        public ShopBuyResult TryResolveBuy(uint playerEntityId, uint shopEntityId, uint itemEntityId)
        {
            if (!_ecs.TryGet<ShopComponent>(shopEntityId, out var shop))
                return Fail("That is not a shopkeeper.");

            var currency = shop!.AcceptedCurrency;

            if (!_ecs.TryGet<ItemDataComponent>(itemEntityId, out var data))
                return Fail("That item does not exist.");

            // Determine pricing: buy-back shelf items cost what the shop paid (SellRatio × Value),
            // standard base stock items cost BuyRatio × Value (resolved decision 5).
            long price;
            if (_ecs.TryGet<ShopStockComponent>(itemEntityId, out var stockComp)
                && stockComp!.Provenance == StockProvenance.Acquired)
            {
                // Buy-back: player recovers the item at the price the shop paid them.
                price = (long)Math.Ceiling(data!.Value * _options.SellRatio);
            }
            else
            {
                price = (long)Math.Ceiling(data!.Value * _options.BuyRatio);
            }

            if (!_walletSystem.CanAfford(playerEntityId, currency, price))
                return new ShopBuyResult(false, price, currency, "You cannot afford that.");

            return new ShopBuyResult(true, price, currency, null);
        }

        // ── TryResolveSell ────────────────────────────────────────────────────────

        public ShopSellResult TryResolveSell(uint playerEntityId, uint shopEntityId, uint itemEntityId)
        {
            if (!_ecs.TryGet<ShopComponent>(shopEntityId, out var shop))
                return FailSell("That is not a shopkeeper.");

            var currency = shop!.AcceptedCurrency;

            if (!_ecs.TryGet<ItemDataComponent>(itemEntityId, out var data))
                return FailSell("That item does not exist.");

            // Refuse valueless items (12a resolved Q: Value==0 means not saleable).
            if (data!.Value <= 0)
                return FailSell("The shopkeeper has no interest in that.");

            var price = (long)Math.Ceiling(data.Value * _options.SellRatio);

            // Check till affordability — sell refuses if the till cannot pay the player.
            if (!_walletSystem.CanAfford(shopEntityId, currency, price))
                return FailSell("The shopkeeper cannot afford to buy that right now.");

            // INV-8: ExpiresAt arithmetic lives here (not in the command).
            var expiresAt = _clock.UtcNow + _options.BuyBackRetention;

            return new ShopSellResult(true, price, currency, expiresAt, null);
        }

        // ── PlanRestock ───────────────────────────────────────────────────────────

        public IReadOnlyList<(string BlueprintId, int Shortfall)> PlanRestock(uint shopEntityId)
        {
            if (!_ecs.TryGet<ShopComponent>(shopEntityId, out var shop))
                return Array.Empty<(string, int)>();

            var result = new List<(string, int)>();

            foreach (var row in shop!.BaseStock)
            {
                if (string.IsNullOrEmpty(row.BlueprintId)) continue;

                // Count live Base entities in the shop's inventory matching this blueprint.
                var liveCount = 0;
                foreach (var itemId in _itemSystem.GetItemsInInventory(shopEntityId))
                {
                    if (!_ecs.TryGet<ShopStockComponent>(itemId, out var sc)) continue;
                    if (sc!.Provenance != StockProvenance.Base) continue;
                    if (!_ecs.TryGet<BlueprintComponent>(itemId, out var bp)) continue;
                    if (bp!.BlueprintId == row.BlueprintId)
                        liveCount++;
                }

                var shortfall = row.Quantity - liveCount;
                if (shortfall > 0)
                    result.Add((row.BlueprintId, shortfall));
            }

            return result;
        }

        // ── FindExpired ───────────────────────────────────────────────────────────

        public IReadOnlyList<uint> FindExpired(uint shopEntityId, DateTime nowUtc)
        {
            var result = new List<uint>();

            foreach (var itemId in _itemSystem.GetItemsInInventory(shopEntityId))
            {
                if (!_ecs.TryGet<ShopStockComponent>(itemId, out var sc)) continue;
                if (sc!.Provenance != StockProvenance.Acquired) continue;
                if (!sc.ExpiresAt.HasValue) continue;
                if (sc.ExpiresAt.Value <= nowUtc)
                    result.Add(itemId);
            }

            return result;
        }

        // ── SeedTill ──────────────────────────────────────────────────────────────

        public void SeedTill(uint shopEntityId)
        {
            if (!_ecs.TryGet<ShopComponent>(shopEntityId, out var shop)) return;

            var seed = shop!.TillSeed > 0 ? shop.TillSeed : _options.DefaultTillSeed;
            if (seed <= 0) return;

            if (!_ecs.TryGet<WalletComponent>(shopEntityId, out var wallet))
            {
                wallet = new WalletComponent();
                _ecs.AddComponent(shopEntityId, wallet);
            }

            wallet!.Balances[shop.AcceptedCurrency] = seed;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static ShopBuyResult Fail(string reason) =>
            new ShopBuyResult(false, 0, CurrencyId.Coin, reason);

        private static ShopSellResult FailSell(string reason) =>
            new ShopSellResult(false, 0, CurrencyId.Coin, null, reason);
    }
}
