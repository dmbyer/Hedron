using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Systems;

namespace Hedron.Core.Modules.Progression.Handlers
{
    /// <summary>
    /// Handles <see cref="MobDiedEvent"/>: resolves the combat XP award via
    /// <see cref="IProgressionSystem.AwardCombatExperience"/> and publishes one
    /// <see cref="ExperienceAwardedEvent"/> per positive-amount track, plus one
    /// <see cref="TrackImprovedEvent"/> per threshold crossed.
    ///
    /// <para>
    /// Priority 20 (<see cref="HandlerPriority.Domain"/>) — independent of <c>SpawnSystem</c>'s
    /// slot-vacancy read and <c>CurrencyLootHandler</c>'s loot roll on the same event; no
    /// inter-handler ordering constraint. All read the live mob pre-destroy.
    /// </para>
    ///
    /// <para><c>KillerEntityId == 0</c> → no attributable killer, discard (no award, no event).</para>
    ///
    /// <para>
    /// INV-8: this handler orchestrates only — the award math and threshold resolution live in
    /// <see cref="IProgressionSystem"/>. No game rule is held here.
    /// </para>
    /// </summary>
    public sealed class ExperienceAwardHandler : IEventHandler<MobDiedEvent>
    {
        private readonly IProgressionSystem _progressionSystem;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public ExperienceAwardHandler(IProgressionSystem progressionSystem, IEventBus eventBus)
        {
            _progressionSystem = progressionSystem;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(MobDiedEvent @event)
        {
            if (@event.KillerEntityId == 0)
                return;

            var result = _progressionSystem.AwardCombatExperience(@event.KillerEntityId, @event.MobEntityId);

            foreach (var row in result.Tracks)
            {
                if (row.AmountAwarded > 0)
                {
                    await _eventBus.PublishAsync(
                        new ExperienceAwardedEvent(@event.KillerEntityId, row.Track, row.AmountAwarded, XpSource.CombatKill))
                        .ConfigureAwait(false);
                }

                for (var i = 0; i < row.ImprovementsGained; i++)
                {
                    await _eventBus.PublishAsync(
                        new TrackImprovedEvent(@event.KillerEntityId, row.Track, row.NewImprovementCount))
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
