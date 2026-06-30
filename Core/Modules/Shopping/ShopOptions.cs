namespace Hedron.Core.Modules.Shopping
{
    /// <summary>
    /// App-wide configuration for the Shopping module. Bound from the <c>Shop:</c> section of
    /// <c>appsettings.json</c> via <c>IOptions&lt;ShopOptions&gt;</c>.
    ///
    /// <para>
    /// Defaults: 5-minute restock, 1-hour buy-back shelf, buy at 2× value, sell at 0.5× value,
    /// till seeded to 100 000 base-unit Coin (~1 000 gold).
    /// </para>
    /// </summary>
    public sealed class ShopOptions
    {
        /// <summary>
        /// How often the heartbeat restock sweep runs (WP-3). Defaults to 5 minutes.
        /// </summary>
        public System.TimeSpan RestockInterval { get; set; } = System.TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long player-sold items remain on the buy-back shelf before being destroyed (WP-3).
        /// Defaults to 1 hour.
        /// </summary>
        public System.TimeSpan BuyBackRetention { get; set; } = System.TimeSpan.FromHours(1);

        /// <summary>
        /// Global multiplier applied to an item's <c>ItemDataComponent.Value</c> to compute the
        /// buy price (player buying from shop). Default 2.0 (shop sells at 2× base value).
        /// </summary>
        public decimal BuyRatio { get; set; } = 2.0m;

        /// <summary>
        /// Global multiplier applied to an item's <c>ItemDataComponent.Value</c> to compute the
        /// sell price (player selling to shop). Default 0.5 (shop pays half the base value).
        /// </summary>
        public decimal SellRatio { get; set; } = 0.5m;

        /// <summary>
        /// Amount of the shopkeeper's accepted currency (base units) deposited into its till on
        /// each spawn when <c>ShopComponent.TillSeed</c> is 0. Default 100 000 Coin (~1 000 gold).
        /// </summary>
        public long DefaultTillSeed { get; set; } = 100_000;
    }
}
