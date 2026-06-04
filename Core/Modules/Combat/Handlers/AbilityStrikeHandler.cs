using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Combat.Handlers
{
    /// <summary>
    /// Renders the fused ability-hit narrative for <see cref="AbilityStrikeResolvedEvent"/> and
    /// conditionally publishes <see cref="CombatEndedEvent"/> for terminal outcomes.
    /// Priority <see cref="HandlerPriority.Domain"/> — same tier as <see cref="CombatHandler"/>,
    /// ensuring narrative lands before <see cref="CombatMobDeathHandler"/> (priority 80) destroys
    /// the entity.
    /// </summary>
    /// <remarks>
    /// Does NOT publish <see cref="CombatRoundEvent"/> — the ability-strike path produces a single
    /// fused message ("{name} kicks {mob} for N damage. [You: …]") rather than the two-message
    /// flow used by the regular combat tick.
    /// </remarks>
    public sealed class AbilityStrikeHandler : IEventHandler<AbilityStrikeResolvedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;
        private readonly IStatSystem _statSystem;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public AbilityStrikeHandler(
            EntityService entityService,
            IBroadcastSystem broadcast,
            IStatSystem statSystem,
            IAbilityRegistry abilityRegistry,
            IEventBus eventBus)
        {
            _entityService = entityService;
            _broadcast = broadcast;
            _statSystem = statSystem;
            _abilityRegistry = abilityRegistry;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(AbilityStrikeResolvedEvent @event)
        {
            var result = @event.Result;
            var damage = result.DamageDealt;
            var defenderName = @event.DefenderName ?? "the creature";

            _abilityRegistry.TryGet(@event.AbilityId, out var abilityDef);
            var abilityName = abilityDef?.Name ?? @event.AbilityId;
            var abilityKind = abilityDef?.Kind ?? AbilityKind.Skill;

            // Build verb phrases.
            string verbPhrase2p;   // second person: "kick", "cast firebolt at"
            string verbPhrase3p;   // third person:  "kicks", "casts firebolt at"
            if (abilityKind == AbilityKind.Skill)
            {
                verbPhrase2p = abilityName.ToLower();
                verbPhrase3p = abilityName.ToLower() + "s";
            }
            else
            {
                var nameLower = abilityName.ToLower();
                verbPhrase2p = $"cast {nameLower} at";
                verbPhrase3p = $"casts {nameLower} at";
            }

            // HP status values for the attacker (player) line.
            var attackerHp = _statSystem.GetCurrentHp(@event.AttackerEntityId);
            var attackerMaxHp = _statSystem.GetMaxHp(@event.AttackerEntityId);
            var defenderHp = _statSystem.GetCurrentHp(@event.DefenderEntityId);
            var defenderMaxHp = _statSystem.GetMaxHp(@event.DefenderEntityId);

            var attackerName = GetMobOrPlayerName(@event.AttackerEntityId);

            // Attacker sees the fused hit + HP bar in one line.
            var attackerMessage = $"You {verbPhrase2p} {defenderName} for {damage} damage. " +
                                  $"[You: {attackerHp}/{attackerMaxHp} HP | {defenderName}: {defenderHp}/{defenderMaxHp} HP]";

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage(attackerMessage, OutputSeverity.System),
                entityId => entityId == @event.AttackerEntityId)
                .ConfigureAwait(false);

            // Observers (and the defender if they have a session) see a plain narrative line.
            var observerMessage = $"{attackerName} {verbPhrase3p} {defenderName} for {damage} damage.";

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage(observerMessage, OutputSeverity.System),
                entityId => entityId != @event.AttackerEntityId)
                .ConfigureAwait(false);

            // Terminal outcomes: hand off to the existing death path via CombatEndedEvent.
            if (result.Outcome == CombatRoundOutcome.MobDied)
            {
                await _eventBus.PublishAsync(new CombatEndedEvent(
                    @event.AttackerEntityId,
                    @event.DefenderEntityId,
                    CombatEndOutcome.MobDied,
                    @event.RoomEntityId,
                    DefenderName: defenderName))
                    .ConfigureAwait(false);
            }
            else if (result.Outcome == CombatRoundOutcome.PlayerIncapacitated)
            {
                await _eventBus.PublishAsync(new CombatEndedEvent(
                    @event.AttackerEntityId,
                    @event.DefenderEntityId,
                    CombatEndOutcome.PlayerIncapacitated,
                    @event.RoomEntityId,
                    DefenderName: defenderName))
                    .ConfigureAwait(false);
            }
        }

        private string GetMobOrPlayerName(uint entityId)
        {
            if (_entityService.TryGet<PlayerComponent>(entityId, out var p))
                return p.DisplayName;
            if (_entityService.TryGet<MobDataComponent>(entityId, out var m))
                return m.Name;
            return "someone";
        }
    }
}
