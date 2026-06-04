using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.EntityState.Systems;

namespace Hedron.Core.Modules.Regeneration.Systems
{
    public sealed class RegenerationSystem : IRegenerationSystem
    {
        // Category-3 balance constants — promotion to configuration lands with the
        // dedicated regeneration use-case (depends on the backlogged robust-config model).
        private const int RegenAmount = 1;
        private const int IdleIntervalTicks = 3;

        private readonly EntityService _entityService;
        private readonly IEntityStateService _entityStateService;
        private readonly IAttributeSystem _attributeSystem;

        public RegenerationSystem(
            EntityService entityService,
            IEntityStateService entityStateService,
            IAttributeSystem attributeSystem)
        {
            _entityService = entityService;
            _entityStateService = entityStateService;
            _attributeSystem = attributeSystem;
        }

        public void ApplyTickRegen(long tickId)
        {
            foreach (var (entityId, _) in _entityService.GetAllComponents<PoolsComponent>())
            {
                if (_entityStateService.IsInState(entityId, EntityStateFlags.InCombat))
                    continue;

                bool isResting = _entityStateService.IsInState(entityId, EntityStateFlags.Resting);

                if (!isResting && tickId % IdleIntervalTicks != 0)
                    continue;

                _attributeSystem.SetCurrentHp(entityId,
                    _attributeSystem.GetCurrentHp(entityId) + RegenAmount);
                _attributeSystem.SetCurrentMana(entityId,
                    _attributeSystem.GetCurrentMana(entityId) + RegenAmount);
                _attributeSystem.SetCurrentStamina(entityId,
                    _attributeSystem.GetCurrentStamina(entityId) + RegenAmount);
                _attributeSystem.SetCurrentAstra(entityId,
                    _attributeSystem.GetCurrentAstra(entityId) + RegenAmount);
            }
        }
    }
}
