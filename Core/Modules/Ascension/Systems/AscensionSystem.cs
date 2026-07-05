using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Ascension.Components;

namespace Hedron.Core.Modules.Ascension.Systems
{
    public sealed class AscensionSystem : IAscensionSystem
    {
        private readonly EntityService _entityService;

        public AscensionSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public int GetTier(uint entityId)
            => _entityService.TryGet<AscensionComponent>(entityId, out var comp) ? comp.Tier : 0;

        public AscendEligibility CanAscend(uint entityId)
            => GetTier(entityId) >= AscensionConstants.MaxTier
                ? AscendEligibility.Blocked(AscendIneligibleReason.AtMaxTier)
                : AscendEligibility.Ok();

        public AscendResult TryAscend(uint entityId)
        {
            var eligibility = CanAscend(entityId);
            if (!eligibility.Eligible)
            {
                var previousTier = GetTier(entityId);
                return new AscendResult(false, previousTier, previousTier, Array.Empty<string>(), eligibility.Reason);
            }

            EnsureComponent(entityId, out var comp);
            var previous = comp.Tier;
            comp.Tier = Math.Min(previous + 1, AscensionConstants.MaxTier);

            var recorded = new List<string>();
            if (AscensionConstants.UnlocksForTier.TryGetValue(comp.Tier, out var unlockIds))
            {
                foreach (var id in unlockIds)
                {
                    if (comp.GrantedUnlocks.Contains(id))
                        continue;
                    comp.GrantedUnlocks.Add(id);
                    recorded.Add(id);
                }
            }

            return new AscendResult(true, previous, comp.Tier, recorded, null);
        }

        public IReadOnlyList<string> GetGrantedUnlocks(uint entityId)
            => _entityService.TryGet<AscensionComponent>(entityId, out var comp)
                ? comp.GrantedUnlocks
                : Array.Empty<string>();

        private void EnsureComponent(uint entityId, out AscensionComponent comp)
        {
            if (!_entityService.TryGet<AscensionComponent>(entityId, out comp))
            {
                comp = new AscensionComponent();
                _entityService.AddComponent(entityId, comp);
            }
        }
    }
}
