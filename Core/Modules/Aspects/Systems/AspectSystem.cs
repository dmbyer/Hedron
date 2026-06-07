using System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Aspects.Systems
{
    public sealed class AspectSystem : IAspectSystem
    {
        private readonly EntityService _entityService;

        public AspectSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public int Resolve(int magnitude, AspectComposition composition, uint attackerEntityId, uint defenderEntityId)
        {
            if (composition.IsEmpty)
                return magnitude;

            double total = 0.0;

            foreach (var (aspect, weight) in composition.Weights)
            {
                double portion = magnitude * (weight / 100.0);

                // Attacker affinity boost: AffinityWeights on the attacker tells us how well
                // the attacker channels this aspect (same dict as the outgoing composition;
                // a 100-weight single-aspect attacker has full affinity in that aspect).
                // boost factor = weight / 100  (0→1)
                var attackerAffinity = GetAffinityWeight(attackerEntityId, aspect);
                double boostFactor = 1.0 + attackerAffinity / 100.0;

                // Defender resist: independent per-aspect resistance [0,100]
                var resist = Math.Clamp(Resist(defenderEntityId, aspect), 0, 100);
                double resistFactor = 1.0 - resist / 100.0;

                total += portion * boostFactor * resistFactor;
            }

            return Math.Max(0, (int)Math.Round(total));
        }

        public AspectComposition Affinity(uint entityId)
        {
            if (!_entityService.TryGet<AspectAffinitiesComponent>(entityId, out var comp) ||
                comp.AffinityWeights.Count == 0)
                return AspectComposition.Empty;

            return new AspectComposition(comp.AffinityWeights);
        }

        public int Resist(uint entityId, AspectId aspect)
        {
            if (!_entityService.TryGet<AspectAffinitiesComponent>(entityId, out var comp))
                return 0;

            return comp.BaseResistances.TryGetValue(aspect, out var resistance)
                ? Math.Clamp(resistance, 0, 100)
                : 0;
        }

        private int GetAffinityWeight(uint entityId, AspectId aspect)
        {
            if (!_entityService.TryGet<AspectAffinitiesComponent>(entityId, out var comp))
                return 0;

            return comp.AffinityWeights.TryGetValue(aspect, out var w) ? w : 0;
        }
    }
}
