using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Progression.Events
{
    /// <summary>
    /// A track gained XP. Thin and frequent (prompt/telemetry consumers) — published once per
    /// awarding action that produced a positive amount, never for a no-op award or a missed
    /// chance roll. <see cref="Track"/> may be a score track or an ability track.
    /// </summary>
    public sealed record ExperienceAwardedEvent(
        uint EntityId,
        ProgressionTrack Track,
        int Amount,
        XpSource Source) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
