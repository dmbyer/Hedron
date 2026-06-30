using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Shopping.Components
{
    /// <summary>
    /// Per-item provenance and expiry marker placed on every item in a shopkeeper's inventory.
    /// World-transient — NOT <c>[Persistent]</c>: base items re-spawn fresh; acquired items are
    /// intentionally dropped on restart so buy-back shelves clear cleanly (INV-23, design note).
    /// </summary>
    public sealed class ShopStockComponent : IComponent
    {
        /// <summary>
        /// How this item entered the shop's inventory.
        /// <see cref="StockProvenance.Base"/> = spawned from the authored base-stock template;
        /// <see cref="StockProvenance.Acquired"/> = sold to the shop by a player.
        /// </summary>
        public StockProvenance Provenance { get; set; } = StockProvenance.Base;

        /// <summary>
        /// UTC time at which the buy-back shelf item expires and is destroyed.
        /// <see langword="null"/> for base-stock items (they never expire; they restock).
        /// Set by the sell command to <c>clock.UtcNow + ShopOptions.BuyBackRetention</c>
        /// (INV-8: the arithmetic is in <c>IShopSystem.TryResolveSell</c>, not the command).
        /// </summary>
        public System.DateTime? ExpiresAt { get; set; } = null;
    }

    /// <summary>
    /// Identifies how a shop item entered the shopkeeper's inventory.
    /// </summary>
    public enum StockProvenance
    {
        /// <summary>Spawned from the shopkeeper's authored base stock. Restocked by the heartbeat sweep.</summary>
        Base = 0,

        /// <summary>Sold to the shop by a player. Expires after <c>ShopOptions.BuyBackRetention</c>.</summary>
        Acquired = 1,
    }
}
