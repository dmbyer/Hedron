using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Combat.Resolvers
{
    /// <summary>
    /// Resolves a mob name/keyword against mobs currently in the invoker's room.
    /// Returns the mob entity id (as string) as the canonical value so that commands
    /// can pass it directly to combat resolution without a second name-lookup.
    /// </summary>
    public sealed class MobInRoomResolver : IArgumentResolver
    {
        private readonly EntityService _entityService;

        public MobInRoomResolver(EntityService entityService)
        {
            _entityService = entityService;
        }

        public IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
                return null;

            var roomEntityId = location.RoomEntityId;
            var candidates = new List<ResolvedCandidate>();

            foreach (var (entityId, mob) in _entityService.GetAllComponents<MobDataComponent>())
            {
                if (!_entityService.TryGet<LocationComponent>(entityId, out var mobLoc) ||
                    mobLoc.RoomEntityId != roomEntityId)
                    continue;

                var canonicalValue = entityId.ToString();
                candidates.Add(new ResolvedCandidate(mob.Name, canonicalValue));
                foreach (var keyword in mob.Keywords)
                    candidates.Add(new ResolvedCandidate(keyword, canonicalValue));
            }

            return candidates;
        }
    }
}
