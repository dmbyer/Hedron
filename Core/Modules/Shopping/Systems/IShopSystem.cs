using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Shopping.Systems
{
    /// <summary>
    /// Domain system that owns all shopping rules: listing, buy/sell validation, buy-back
    /// pricing, restock planning, and expiry detection. Pure — returns results only; never
    /// publishes events or calls persistence (INV-5). Composes <c>IWalletSystem</c>,
    /// <c>IItemSystem</c>, and <c>IClock</c>.
    ///
    /// <para>
    /// Prices are computed on read from <c>ItemDataComponent.Value</c> × ratio (never stored).
    /// All time comparisons resolve through the injected <see cref="Hedron.Core.Systems.IClock"/>
    /// (INV-26).
    /// </para>
    /// </summary>
    public interface IShopSystem
    {
        /// <summary>
        /// Returns the full shop listing for <paramref name="shopEntityId"/>: base-stock rows
        /// (Provenance = Base) followed by acquired rows (Provenance = Acquired), each with a
        /// compute-on-read buy price. No state mutation.
        /// </summary>
        ShopListing GetListing(uint shopEntityId);

        /// <summary>
        /// Validates whether <paramref name="playerEntityId"/> can buy <paramref name="itemEntityId"/>
        /// from <paramref name="shopEntityId"/>: checks player affordability and computes the price.
        /// Returns a <see cref="ShopBuyResult"/> indicating success/failure and the computed price.
        /// No state mutation.
        /// </summary>
        /// <remarks>
        /// Buy-back pricing: if the item carries <c>ShopStockComponent { Provenance = Acquired }</c>
        /// the price is the sell price the shop previously paid (<c>Value × SellRatio</c>), not the
        /// standard buy price — resolved decision 5 (fair mistake-protection).
        /// </remarks>
        ShopBuyResult TryResolveBuy(uint playerEntityId, uint shopEntityId, uint itemEntityId);

        /// <summary>
        /// Validates whether <paramref name="playerEntityId"/> can sell <paramref name="itemEntityId"/>
        /// to <paramref name="shopEntityId"/>: rejects <c>Value == 0</c> items; checks till
        /// affordability; computes the sell price and the clock-derived <c>ExpiresAt</c> for the
        /// resulting <c>ShopStockComponent</c> (INV-8: the <c>now + retention</c> arithmetic is
        /// here, not in the command). No state mutation.
        /// </summary>
        ShopSellResult TryResolveSell(uint playerEntityId, uint shopEntityId, uint itemEntityId);

        /// <summary>
        /// Computes the restock shortfall for <paramref name="shopEntityId"/>: for each base-stock
        /// row in <c>ShopComponent.BaseStock</c>, counts the live entities carrying
        /// <c>ShopStockComponent { Provenance = Base }</c> for that blueprint id in the shop's
        /// inventory, and returns <c>(blueprintId, authoredQty − liveCount)</c> rows where the
        /// shortfall is &gt; 0. Zero-shortfall rows are omitted. Acquired items are ignored.
        /// No state mutation (pure decision; the caller spawns).
        /// </summary>
        IReadOnlyList<(string BlueprintId, int Shortfall)> PlanRestock(uint shopEntityId);

        /// <summary>
        /// Returns the entity ids of all <c>Acquired</c> items in <paramref name="shopEntityId"/>'s
        /// inventory whose <c>ShopStockComponent.ExpiresAt &lt;= <paramref name="nowUtc"/></c>.
        /// Base items and not-yet-expired acquired items are excluded.
        /// No state mutation (pure query; the caller destroys).
        /// </summary>
        IReadOnlyList<uint> FindExpired(uint shopEntityId, DateTime nowUtc);

        /// <summary>
        /// Seeds the shopkeeper's till (<c>WalletComponent</c>) with the configured amount
        /// (<c>ShopComponent.TillSeed</c> or <c>ShopOptions.DefaultTillSeed</c> when the
        /// per-shop seed is 0). A no-op when both seeds are 0.
        /// </summary>
        void SeedTill(uint shopEntityId);
    }
}
