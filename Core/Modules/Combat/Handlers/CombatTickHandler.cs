using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Modules.Time.Events;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Combat.Handlers
{
    /// <summary>
    /// Bridge between the time system and the combat domain. On each heartbeat tick,
    /// processes all active combat pairs via <see cref="ICombatSystem.ExecuteRound"/> and
    /// publishes per-round and terminal outcome events.
    /// Priority 20 (<see cref="HandlerPriority.Domain"/>).
    /// </summary>
    public sealed class CombatTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly ICombatSystem _combatSystem;
        private readonly IEntityStateService _entityStateService;
        private readonly IDeathSystem _deathSystem;
        private readonly IStatSystem _statSystem;
        private readonly IEventBus _eventBus;
        private readonly ILogger<CombatTickHandler> _logger;

        public int Priority => HandlerPriority.Domain;

        public CombatTickHandler(
            EntityService entityService,
            ICombatSystem combatSystem,
            IEntityStateService entityStateService,
            IDeathSystem deathSystem,
            IStatSystem statSystem,
            IEventBus eventBus,
            ILogger<CombatTickHandler> logger)
        {
            _entityService = entityService;
            _combatSystem = combatSystem;
            _entityStateService = entityStateService;
            _deathSystem = deathSystem;
            _statSystem = statSystem;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task HandleAsync(HeartbeatTickEvent @event)
        {
            // Snapshot all entities with CombatStateComponent before iterating — avoid mutation during enumeration.
            var combatants = _entityService.GetAllComponents<CombatStateComponent>().ToList();

            foreach (var (entityId, state) in combatants)
            {
                // Deduplicate: only process when entityId < opponentEntityId so each pair is handled once.
                if (entityId >= state.OpponentEntityId)
                    continue;

                var attackerEntityId = entityId;
                var defenderEntityId = state.OpponentEntityId;

                if (!_entityService.TryGet<LocationComponent>(attackerEntityId, out var loc))
                    continue;

                var roomEntityId = loc.RoomEntityId;

                // Capture HP before the round so IDeathSystem.OnHpChanged has accurate previousHp.
                var defenderHpBefore = _statSystem.GetCurrentHp(defenderEntityId);
                var result = _combatSystem.ExecuteRound(attackerEntityId, defenderEntityId);

                await _eventBus.PublishAsync(new CombatRoundEvent(
                    attackerEntityId, defenderEntityId, roomEntityId, result))
                    .ConfigureAwait(false);

                if (result.Outcome == CombatRoundOutcome.MobDied)
                {
                    var mobName = _entityService.TryGet<MobDataComponent>(defenderEntityId, out var mobData)
                        ? mobData.Name
                        : "the creature";

                    _combatSystem.EndCombat(attackerEntityId, defenderEntityId);

                    await _eventBus.PublishAsync(new CombatEndedEvent(
                        attackerEntityId, defenderEntityId,
                        CombatEndOutcome.MobDied, roomEntityId,
                        DefenderName: mobName))
                        .ConfigureAwait(false);
                }
                else if (result.Outcome == CombatRoundOutcome.PlayerIncapacitated)
                {
                    _combatSystem.EndCombat(attackerEntityId, defenderEntityId);
                    _entityStateService.ExitState(attackerEntityId, EntityStateFlags.InCombat);
                    _entityStateService.ExitState(defenderEntityId, EntityStateFlags.InCombat);

                    await _eventBus.PublishAsync(new CombatEndedEvent(
                        attackerEntityId, defenderEntityId,
                        CombatEndOutcome.PlayerIncapacitated, roomEntityId))
                        .ConfigureAwait(false);

                    var defenderHpAfter = _statSystem.GetCurrentHp(defenderEntityId);
                    var transition = _deathSystem.OnHpChanged(defenderEntityId, defenderHpBefore, defenderHpAfter);
                    if (transition == DeathTransition.BecameIncapacitated)
                    {
                        await _eventBus.PublishAsync(
                            new PlayerIncapacitatedEvent(defenderEntityId, roomEntityId))
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    // Counterattack: the entity that was struck this round now strikes back.
                    var attackerHpBefore = _statSystem.GetCurrentHp(attackerEntityId);
                    var counterResult = _combatSystem.ExecuteRound(defenderEntityId, attackerEntityId);

                    await _eventBus.PublishAsync(new CombatRoundEvent(
                        defenderEntityId, attackerEntityId, roomEntityId, counterResult))
                        .ConfigureAwait(false);

                    if (counterResult.Outcome == CombatRoundOutcome.PlayerIncapacitated)
                    {
                        _combatSystem.EndCombat(defenderEntityId, attackerEntityId);
                        _entityStateService.ExitState(defenderEntityId, EntityStateFlags.InCombat);
                        _entityStateService.ExitState(attackerEntityId, EntityStateFlags.InCombat);

                        await _eventBus.PublishAsync(new CombatEndedEvent(
                            defenderEntityId, attackerEntityId,
                            CombatEndOutcome.PlayerIncapacitated, roomEntityId))
                            .ConfigureAwait(false);

                        var attackerHpAfter = _statSystem.GetCurrentHp(attackerEntityId);
                        var transition = _deathSystem.OnHpChanged(attackerEntityId, attackerHpBefore, attackerHpAfter);
                        if (transition == DeathTransition.BecameIncapacitated)
                        {
                            await _eventBus.PublishAsync(
                                new PlayerIncapacitatedEvent(attackerEntityId, roomEntityId))
                                .ConfigureAwait(false);
                        }
                    }
                }
            }
        }
    }
}
