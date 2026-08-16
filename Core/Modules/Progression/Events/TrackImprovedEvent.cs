using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Progression.Events
{
    /// <summary>
    /// A track crossed a threshold — a power step for a score track, a rank for an ability track
    /// (which grants no power, D3). The discrete milestone future slices (prompt highlight,
    /// achievements, sim labeling) subscribe to. Published once per threshold crossing; a single
    /// award that vaults N thresholds publishes N of these.
    /// </summary>
    public sealed record TrackImprovedEvent(
        uint EntityId,
        ProgressionTrack Track,
        int NewImprovementCount) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
