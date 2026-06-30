using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Shopping.Events
{
    /// <summary>
    /// Published by <c>SellCommand</c> after a player successfully sells an item to a shopkeeper.
    /// Thin payload (INV-8); narration and persistence-pool transition are driven by subscribers.
    ///
    /// <para>
    /// Subscribers:
    /// <list type="bullet">
    ///   <item><c>ItemContextHandler</c> (priority Domain=20) — removes <c>PersistentEntity</c>
    ///         from the item (mirrors drop), so the buy-back shelf item is world-transient and
    ///         drops on restart.</item>
    ///   <item><c>ShopInteractionHandler</c> (priority Notification=80) — narrates the sale to
    ///         the room.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed record ItemSoldEvent(
        uint PlayerEntityId,
        uint ShopEntityId,
        uint ItemEntityId,
        uint RoomEntityId,
        long PriceReceived,
        CurrencyId Currency) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
