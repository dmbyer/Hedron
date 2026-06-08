using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Combat.Systems
{
    public sealed class CombatSystem : ICombatSystem
    {
        private readonly EntityService _entityService;
        private readonly IStatSystem _statSystem;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IAspectSystem _aspectSystem;
        private readonly IRandom _random;

        public CombatSystem(
            EntityService entityService,
            IStatSystem statSystem,
            IAttributeSystem attributeSystem,
            IAspectSystem aspectSystem,
            IRandom random)
        {
            _entityService = entityService;
            _statSystem = statSystem;
            _attributeSystem = attributeSystem;
            _aspectSystem = aspectSystem;
            _random = random;
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
            var roll = _random.Next(1, 21) + _statSystem.GetEffectiveBody(attackerEntityId) / 2;
            var defenseThreshold = 10 + _statSystem.GetEffectiveDefense(defenderEntityId);
            var hit = roll >= defenseThreshold;

            if (!hit)
            {
                return new CombatRoundResult(
                    attackerEntityId,
                    defenderEntityId,
                    DamageDealt: 0,
                    AttackerHit: false,
                    Outcome: CombatRoundOutcome.Miss,
                    AspectComposition: null);
            }

            var attackPower = _statSystem.GetEffectiveAttackPower(attackerEntityId);
            var rawDamage = _random.Next(1, attackPower + 2);

            // Composition source for melee: the attacker's entity affinity (empty = untyped).
            var composition = _aspectSystem.Affinity(attackerEntityId);
            var damage = _aspectSystem.Resolve(rawDamage, composition, attackerEntityId, defenderEntityId);

            return ApplyDamageAndBuildResult(attackerEntityId, defenderEntityId, damage, composition);
        }

        public CombatRoundResult ResolveAbilityStrike(
            uint attackerEntityId,
            uint defenderEntityId,
            int basePower,
            AspectComposition? composition = null)
        {
            // Ability strikes always land — no hit/miss roll.
            var rawDamage = System.Math.Max(1, _random.Next(1, basePower + 2) - _statSystem.GetEffectiveDefense(defenderEntityId));

            var comp = composition ?? AspectComposition.Empty;
            var damage = _aspectSystem.Resolve(rawDamage, comp, attackerEntityId, defenderEntityId);

            return ApplyDamageAndBuildResult(attackerEntityId, defenderEntityId, damage, comp);
        }

        private CombatRoundResult ApplyDamageAndBuildResult(
            uint attackerEntityId,
            uint defenderEntityId,
            int damage,
            AspectComposition composition)
        {
            var currentHp = _statSystem.GetCurrentHp(defenderEntityId);
            _attributeSystem.SetCurrentHp(defenderEntityId, currentHp - damage);

            var hpAfter = _statSystem.GetCurrentHp(defenderEntityId);
            CombatRoundOutcome outcome;

            if (hpAfter <= 0 && _entityService.HasComponent<MobDataComponent>(defenderEntityId))
                outcome = CombatRoundOutcome.MobDied;
            else if (hpAfter <= 0 && _entityService.HasComponent<CharacterComponent>(defenderEntityId))
                outcome = CombatRoundOutcome.PlayerIncapacitated;
            else
                outcome = CombatRoundOutcome.Hit;

            return new CombatRoundResult(
                attackerEntityId,
                defenderEntityId,
                DamageDealt: damage,
                AttackerHit: true,
                Outcome: outcome,
                AspectComposition: composition.IsEmpty ? null : composition);
        }
    }
}
