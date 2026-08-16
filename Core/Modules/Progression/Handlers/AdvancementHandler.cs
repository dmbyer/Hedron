using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Systems;

namespace Hedron.Core.Modules.Progression.Handlers
{
    /// <summary>
    /// The single advancement orchestrator: every XP trigger arrives here, is mapped mechanically
    /// into a <see cref="UseAwardContext"/>, and is resolved by
    /// <see cref="IProgressionSystem.AwardUseExperience"/> against the advancement-rule table.
    /// Publishes one <see cref="ExperienceAwardedEvent"/> per positive row and one
    /// <see cref="TrackImprovedEvent"/> per threshold crossed.
    ///
    /// <para>
    /// Replaces the per-source handler pattern at its third repetition (INV-19). Adding a fourth
    /// XP source is a rule row plus a trigger mapping here — never a fourth handler.
    /// </para>
    ///
    /// <para>
    /// Priority 20 (<see cref="HandlerPriority.Domain"/>) — independent of <c>SpawnSystem</c>'s
    /// slot-vacancy read and <c>CurrencyLootHandler</c>'s loot roll on <see cref="MobDiedEvent"/>;
    /// no inter-handler ordering constraint. All read the live mob pre-destroy.
    /// </para>
    ///
    /// <para>
    /// INV-8: <b>no game rule is held here</b> — not even a discard. "An unattributable killer
    /// awards nothing", "a zero-damage round awards nothing" and "only characters progress" are
    /// <see cref="AdvancementEligibility"/> data on the rule, evaluated inside the system. This
    /// handler's entire job is the field mapping <i>event → context</i>.
    /// </para>
    /// </summary>
    public sealed class AdvancementHandler :
        IEventHandler<MobDiedEvent>,
        IEventHandler<AbilityActivatedEvent>,
        IEventHandler<CombatRoundEvent>,
        IEventHandler<AbilityStrikeResolvedEvent>
    {
        private readonly IProgressionSystem _progressionSystem;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public AdvancementHandler(
            IProgressionSystem progressionSystem,
            IAbilityRegistry abilityRegistry,
            IEventBus eventBus)
        {
            _progressionSystem = progressionSystem;
            _abilityRegistry = abilityRegistry;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Kill → the <see cref="XpSource.CombatKill"/> row, via the
        /// <see cref="IProgressionSystem.AwardCombatExperience"/> wrapper so the victim's
        /// per-mob <c>XpScale</c> is resolved on the system side (and stays shared with the
        /// balance sandbox).
        /// </summary>
        public Task HandleAsync(MobDiedEvent @event)
        {
            var result = _progressionSystem.AwardCombatExperience(@event.KillerEntityId, @event.MobEntityId);
            return PublishAsync(@event.KillerEntityId, XpSource.CombatKill, result.Tracks);
        }

        /// <summary>
        /// Ability activation → the <see cref="XpSource.AbilityUse"/> row for the actor. The
        /// ability's own track comes from the event; its content scale and attribute track come
        /// from the definition.
        /// </summary>
        public Task HandleAsync(AbilityActivatedEvent @event)
        {
            var contentScale = 1.0;
            Hedron.Core.Modules.Stats.ScoreId? attributeTrack = null;
            if (_abilityRegistry.TryGet(@event.AbilityId, out var definition))
            {
                contentScale = definition.XpScale;
                attributeTrack = definition.XpAttributeTrack;
            }

            var result = _progressionSystem.AwardUseExperience(
                @event.ActorEntityId,
                XpSource.AbilityUse,
                new UseAwardContext(
                    SubjectAbilityId: @event.AbilityId,
                    SubjectAttributeTrack: attributeTrack,
                    ContentScale: contentScale));

            return PublishAsync(@event.ActorEntityId, XpSource.AbilityUse, result.Tracks);
        }

        /// <summary>Melee round → the <see cref="XpSource.DamageTaken"/> row for the <b>defender</b>.</summary>
        public Task HandleAsync(CombatRoundEvent @event)
            => AwardDamageTakenAsync(@event.DefenderEntityId, @event.Result.DamageDealt);

        /// <summary>Ability strike → the <see cref="XpSource.DamageTaken"/> row for the <b>defender</b>.</summary>
        public Task HandleAsync(AbilityStrikeResolvedEvent @event)
            => AwardDamageTakenAsync(@event.DefenderEntityId, @event.Result.DamageDealt);

        // ── Internals ────────────────────────────────────────────────────────────

        private Task AwardDamageTakenAsync(uint defenderEntityId, int damageDealt)
        {
            var result = _progressionSystem.AwardUseExperience(
                defenderEntityId,
                XpSource.DamageTaken,
                new UseAwardContext(Magnitude: damageDealt));

            return PublishAsync(defenderEntityId, XpSource.DamageTaken, result.Tracks);
        }

        private async Task PublishAsync(uint earnerEntityId, XpSource source, IReadOnlyList<AwardOutcome> rows)
        {
            foreach (var row in rows)
            {
                if (row.AmountAwarded > 0)
                {
                    await _eventBus.PublishAsync(
                        new ExperienceAwardedEvent(earnerEntityId, row.Track, row.AmountAwarded, source))
                        .ConfigureAwait(false);
                }

                for (var i = 0; i < row.ImprovementsGained; i++)
                {
                    await _eventBus.PublishAsync(
                        new TrackImprovedEvent(earnerEntityId, row.Track, row.NewImprovementCount))
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
