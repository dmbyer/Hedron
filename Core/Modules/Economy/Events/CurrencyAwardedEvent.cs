using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Economy.Events
{
    /// <summary>
    /// Published by <c>CurrencyLootHandler</c> (and future reward call sites) after
    /// a successful <c>IWalletSystem.Deposit</c>. One event per currency awarded.
    /// Past-tense thin fact — carries only the recipient, currency, and amount (INV-5).
    /// </summary>
    public sealed record CurrencyAwardedEvent(
        uint RecipientEntityId,
        CurrencyId Currency,
        long Amount) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
