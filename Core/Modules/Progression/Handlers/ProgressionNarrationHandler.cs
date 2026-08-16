using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Handlers
{
    /// <summary>
    /// Writes the "you are getting better" lines — one per XP award, one per improvement — to the
    /// earning player and nobody else, each gated on its own <see cref="PreferenceId"/> so a player
    /// can silence either class without losing the underlying progression.
    ///
    /// <para>
    /// Pure presentation: no state mutation, no domain call beyond
    /// <see cref="IPreferenceSystem"/> and <see cref="IBroadcastSystem"/>. Priority 80
    /// (<see cref="HandlerPriority.Notification"/>), so it runs after
    /// <see cref="AdvancementHandler"/> has already accrued the XP.
    /// </para>
    ///
    /// <para>
    /// Uses the new <see cref="IBroadcastSystem.SendToEntityAsync"/> rather than the
    /// room-broadcast-with-predicate workaround — the earner is addressed by id, so the line
    /// arrives even if they carry no location.
    /// </para>
    /// </summary>
    public sealed class ProgressionNarrationHandler :
        IEventHandler<ExperienceAwardedEvent>,
        IEventHandler<TrackImprovedEvent>
    {
        private readonly IPreferenceSystem _preferences;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Notification;

        public ProgressionNarrationHandler(IPreferenceSystem preferences, IBroadcastSystem broadcast)
        {
            _preferences = preferences;
            _broadcast = broadcast;
        }

        public Task HandleAsync(ExperienceAwardedEvent @event)
        {
            if (!_preferences.IsEnabled(@event.EntityId, PreferenceId.ProgressionXpMessages))
                return Task.CompletedTask;

            var text = @event.Track.IsAbility
                ? $"You feel more practiced with {@event.Track.AbilityId}."
                : $"You feel your {@event.Track.Score} grow stronger.";

            return SendAsync(@event.EntityId, text);
        }

        public Task HandleAsync(TrackImprovedEvent @event)
        {
            if (!_preferences.IsEnabled(@event.EntityId, PreferenceId.ProgressionImprovementMessages))
                return Task.CompletedTask;

            var text = @event.Track.IsAbility
                ? $"Your mastery of {@event.Track.AbilityId} improves! (rank {@event.NewImprovementCount})"
                : $"Your {@event.Track.Score} improves! ({@event.NewImprovementCount})";

            return SendAsync(@event.EntityId, text);
        }

        private Task SendAsync(uint entityId, string text)
            => _broadcast.SendToEntityAsync(
                entityId,
                new PlainMessage(text, OutputSeverity.System, OutputCategory.System));
    }
}
