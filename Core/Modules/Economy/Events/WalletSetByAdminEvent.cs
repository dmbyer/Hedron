using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Economy.Events
{
    /// <summary>
    /// Published by <c>SetwalletCommand</c> after an admin absolute-sets a player's wallet balance.
    /// Past-tense, thin fact — carries the four identifiers needed for the audit log.
    /// Subscribers: <c>AdminAuditHandler</c>.
    /// </summary>
    public sealed record WalletSetByAdminEvent(
        uint AdminEntityId,
        uint TargetEntityId,
        CurrencyId Currency,
        long Amount) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
