using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Ascension.Events
{
    /// <summary>
    /// Milestone fact — an entity's tier changed. Published once per successful ascend by
    /// <c>AscendCommand</c> (Initiator), never by <see cref="Systems.IAscensionSystem"/> (INV-5/INV-8).
    /// Drives narration now; a future unlock-grant handler and band re-tag/telemetry/achievements
    /// are later consumers.
    /// </summary>
    public sealed record AscendedEvent(
        uint EntityId,
        int NewTier,
        int PreviousTier) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
