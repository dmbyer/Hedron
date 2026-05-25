using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;

namespace Hedron.Core.Modules.Items.Resolvers
{
    /// <summary>
    /// Resolves an item name/keyword against items currently on the ground in the invoker's room.
    /// Emits one <see cref="ResolvedCandidate"/> per match string (item name + each keyword),
    /// all sharing the same <see cref="ResolvedCandidate.CanonicalValue"/> (the item's display name).
    /// </summary>
    public sealed class ItemInRoomResolver : IArgumentResolver
    {
        private readonly IItemSystem _itemSystem;
        private readonly EntityService _entityService;

        public ItemInRoomResolver(IItemSystem itemSystem, EntityService entityService)
        {
            _itemSystem = itemSystem;
            _entityService = entityService;
        }

        public IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
                return null;

            var itemIds = _itemSystem.GetItemsInRoom(location.RoomEntityId);
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
