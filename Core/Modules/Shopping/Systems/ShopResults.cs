using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Shopping.Systems
{
    // ── GetListing result ────────────────────────────────────────────────────────

    /// <summary>
    /// A single row in the shop listing returned by <see cref="IShopSystem.GetListing"/>.
    /// Price is computed-on-read from <c>ItemDataComponent.Value</c> × ratio.
    /// </summary>
    public sealed record ShopListingRow(
        uint ItemEntityId,
        string Name,
        long BuyPrice,
        CurrencyId Currency,
        bool IsAcquired);

    /// <summary>
    /// The complete listing for a shop: base stock rows followed by acquired (buy-back) rows.
    /// Returned by <see cref="IShopSystem.GetListing"/>.
    /// </summary>
    public sealed record ShopListing(
        uint ShopEntityId,
        CurrencyId Currency,
        IReadOnlyList<ShopListingRow> Rows);

    // ── TryResolveBuy result ─────────────────────────────────────────────────────

    /// <summary>
    /// Outcome of <see cref="IShopSystem.TryResolveBuy"/>. Pure — no mutation occurs.
    /// </summary>
    public sealed record ShopBuyResult(
        bool Success,
        long Price,
        CurrencyId Currency,
        string? FailureReason);

    // ── TryResolveSell result ────────────────────────────────────────────────────

    /// <summary>
    /// Outcome of <see cref="IShopSystem.TryResolveSell"/>. Pure — no mutation occurs.
    /// Carries the clock-derived <see cref="ExpiresAt"/> that the sell command stamps onto
    /// <c>ShopStockComponent</c> (INV-8: the <c>now + retention</c> arithmetic lives in the system).
    /// </summary>
    public sealed record ShopSellResult(
        bool Success,
        long Price,
        CurrencyId Currency,
        DateTime? ExpiresAt,
        string? FailureReason);
}
