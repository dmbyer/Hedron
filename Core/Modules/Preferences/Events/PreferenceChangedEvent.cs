using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Preferences.Events
{
    /// <summary>
    /// A player changed one of their settings. Thin past-tense fact — published by
    /// <c>ConfigCommand</c> after <c>IPreferenceSystem.Set</c> so downstream surfaces (telemetry,
    /// a future prompt recomposition) can react without polling the component.
    /// </summary>
    public sealed record PreferenceChangedEvent(
        uint EntityId,
        PreferenceId Preference,
        bool Enabled) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
