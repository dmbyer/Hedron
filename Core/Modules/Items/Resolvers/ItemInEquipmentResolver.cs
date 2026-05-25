using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;

namespace Hedron.Core.Modules.Items.Resolvers
{
    /// <summary>
    /// Resolves an item name/keyword against items currently worn by the invoker.
    /// Emits one <see cref="ResolvedCandidate"/> per match string (item name + each keyword),
    /// all sharing the same <see cref="ResolvedCandidate.CanonicalValue"/> (the item's display name).
    /// </summary>
    public sealed class ItemInEquipmentResolver : IArgumentResolver
    {
        private readonly IEquipmentSystem _equipmentSystem;
        private readonly EntityService _entityService;

        public ItemInEquipmentResolver(IEquipmentSystem equipmentSystem, EntityService entityService)
        {
            _equipmentSystem = equipmentSystem;
            _entityService = entityService;
        }

        public IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context)
        {
            var itemIds = _equipmentSystem.GetEquippedItems(context.InvokerEntityId);
            var candidates = new List<ResolvedCandidate>(itemIds.Count * 2);

            foreach (var itemId in itemIds)
            {
                if (!_entityService.TryGet<ItemDataComponent>(itemId, out var data)) continue;

                candidates.Add(new ResolvedCandidate(data.Name, data.Name));
                foreach (var keyword in data.Keywords)
                    candidates.Add(new ResolvedCandidate(keyword, data.Name));
            }

            return candidates;
        }
    }
}
