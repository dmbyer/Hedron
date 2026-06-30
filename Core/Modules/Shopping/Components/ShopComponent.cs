using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Shopping.Components
{
    /// <summary>
    /// Marks a mob entity as a shopkeeper. Presence = "this mob trades" (INV-4 — use
    /// <c>HasComponent&lt;ShopComponent&gt;</c>, never <c>is</c>/<c>as</c>).
    ///
    /// <para>
    /// Holds the accepted currency, the till seed used on each spawn, an optional per-shop
    /// price-ratio override (deferred — backlog; field is present but unused by <c>IShopSystem</c>
    /// in WP-1), and the authored base-stock rows <c>(blueprintId, quantity)</c> the spawn path
    /// fills on startup.
    /// </para>
    ///
    /// <para>
    /// World-content component — NOT <c>[Persistent]</c>. The durable form is the mob YAML
    /// template (<c>shop:</c> block). The till (<c>WalletComponent</c>) is tagged
    /// <c>[Persistent]</c> but is never written for a shopkeeper because the mob carries no
    /// <c>PersistentEntity</c> (two-level opt-in, INV-14). Both reset to the authored seed
    /// on each spawn/restart.
    /// </para>
    /// </summary>
    public sealed class ShopComponent : IComponent
    {
        /// <summary>Currency this shop trades in. Defaults to <see cref="CurrencyId.Coin"/>.</summary>
        public CurrencyId AcceptedCurrency { get; set; } = CurrencyId.Coin;

        /// <summary>
        /// Amount of <see cref="AcceptedCurrency"/> (base units) deposited into the shopkeeper's
        /// <c>WalletComponent</c> (till) on each spawn. 0 means use <see cref="ShopOptions.DefaultTillSeed"/>.
        /// </summary>
        public long TillSeed { get; set; } = 0;

        /// <summary>
        /// Optional per-shop price-ratio override. When <see langword="null"/>, the global
        /// <see cref="ShopOptions.BuyRatio"/> / <see cref="ShopOptions.SellRatio"/> are used.
        /// Deferred (backlog) — field is present but unused in WP-1/WP-2; reserved for future
        /// per-shop pricing.
        /// </summary>
        public decimal? RatioOverride { get; set; } = null;

        /// <summary>
        /// Authored base-stock: the blueprint ids and quantities the shop should maintain.
        /// The spawn path stamps each live entity with <c>ShopStockComponent { Base }</c>;
        /// the restock sweep (WP-3) tops up to these levels.
        /// </summary>
        public List<ShopStockRow> BaseStock { get; set; } = new();
    }

    /// <summary>
    /// A single authored base-stock row: spawn <paramref name="Quantity"/> entities from
    /// blueprint <paramref name="BlueprintId"/> into the shop's inventory.
    /// </summary>
    public sealed class ShopStockRow
    {
        public string BlueprintId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
    }
}
