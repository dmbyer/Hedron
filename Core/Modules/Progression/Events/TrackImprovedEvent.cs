using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression.Events
{
    /// <summary>
    /// A track crossed a threshold into a power step — the discrete milestone future slices
    /// (prompt highlight, achievements, sim labeling) subscribe to. Published once per threshold
    /// crossing; a single award that vaults N thresholds publishes N of these.
    /// </summary>
    public sealed record TrackImprovedEvent(
        uint EntityId,
        ScoreId Track,
        int NewImprovementCount) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
