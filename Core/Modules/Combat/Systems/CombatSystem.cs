using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Stats.Systems;

namespace Hedron.Core.Modules.Combat.Systems
{
    public sealed class CombatSystem : ICombatSystem
    {
        private readonly EntityService _entityService;
        private readonly IStatSystem _statSystem;
        private readonly IAttributeSystem _attributeSystem;

        public CombatSystem(
            EntityService entityService,
            IStatSystem statSystem,
            IAttributeSystem attributeSystem)
        {
            _entityService = entityService;
            _statSystem = statSystem;
            _attributeSystem = attributeSystem;
        }

        public bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId)
        {
            var lower = token.ToLowerInvariant();

            foreach (var (entityId, mob) in _entityService.GetAllComponents<MobDataComponent>())
            {
                if (!_entityService.TryGet<LocationComponent>(entityId, out var loc) ||
                    loc.RoomEntityId != roomEntityId)
                    continue;

                if (mob.Name.StartsWith(lower, System.StringComparison.OrdinalIgnoreCase))
                {
                    mobEntityId = entityId;
                    return true;
                }

                foreach (var keyword in mob.Keywords)
                {
                    if (keyword.StartsWith(lower, System.StringComparison.OrdinalIgnoreCase))
                    {
                        mobEntityId = entityId;
                        return true;
                    }
                }
            }

            mobEntityId = 0;
            return false;
        }

        public void StartCombat(uint attackerEntityId, uint defenderEntityId)
        {
            _entityService.AddComponent(attackerEntityId, new CombatStateComponent { OpponentEntityId = defenderEntityId });
            _entityService.AddComponent(defenderEntityId, new CombatStateComponent { OpponentEntityId = attackerEntityId });
        }

        public void EndCombat(uint attackerEntityId, uint defenderEntityId)
        {
            _entityService.RemoveComponent<CombatStateComponent>(attackerEntityId);
            _entityService.RemoveComponent<CombatStateComponent>(defenderEntityId);
        }

        public CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId)
        {
            var roll = Random.Shared.Next(1, 21) + _statSystem.GetEffectiveBody(attackerEntityId) / 2;
            var defenseThreshold = 10 + _statSystem.GetEffectiveDefense(defenderEntityId);
            var hit = roll >= defenseThreshold;

            if (!hit)
            {
                return new CombatRoundResult(
                    attackerEntityId,
                    defenderEntityId,
                    DamageDealt: 0,
                    AttackerHit: false,
                    Outcome: CombatRoundOutcome.Miss);
            }

            var attackPower = _statSystem.GetEffectiveAttackPower(attackerEntityId);
            var damage = Random.Shared.Next(1, attackPower + 2);

            var currentHp = _statSystem.GetCurrentHp(defenderEntityId);
            _attributeSystem.SetCurrentHp(defenderEntityId, currentHp - damage);

            var hpAfter = _statSystem.GetCurrentHp(defenderEntityId);
            CombatRoundOutcome outcome;

            if (hpAfter == 0 && _entityService.HasComponent<MobDataComponent>(defenderEntityId))
                outcome = CombatRoundOutcome.MobDied;
            else if (hpAfter == 0 && _entityService.HasComponent<CharacterComponent>(defenderEntityId))
                outcome = CombatRoundOutcome.PlayerIncapacitated;
            else
                outcome = CombatRoundOutcome.Hit;

            return new CombatRoundResult(
                attackerEntityId,
                defenderEntityId,
                DamageDealt: damage,
                AttackerHit: true,
                Outcome: outcome);
        }
    }
}
