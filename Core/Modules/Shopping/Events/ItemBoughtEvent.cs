using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Shopping.Events
{
    /// <summary>
    /// Published by <c>BuyCommand</c> after a player successfully buys an item from a shopkeeper
    /// (base stock or buy-back shelf). Thin payload (INV-8); narration and persistence-pool
    /// transition are driven by subscribers.
    ///
    /// <para>
    /// Subscribers:
    /// <list type="bullet">
    ///   <item><c>ItemContextHandler</c> (priority Domain=20) — adds <c>PersistentEntity</c> to the
    ///         item; <b>keeps</b> <c>BlueprintComponent</c> as an origin record (INV-21); removes
    ///         <c>ShopStockComponent</c>.</item>
    ///   <item><c>ShopInteractionHandler</c> (priority Notification=80) — narrates the purchase to
    ///         the room.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed record ItemBoughtEvent(
        uint PlayerEntityId,
        uint ShopEntityId,
        uint ItemEntityId,
        uint RoomEntityId,
        long PricePaid,
        CurrencyId Currency) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
