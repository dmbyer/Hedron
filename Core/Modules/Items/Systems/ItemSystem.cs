using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Items.Systems
{
    public sealed class ItemSystem : IItemSystem
    {
        private readonly EntityService _entityService;

        public ItemSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public IReadOnlyList<uint> GetItemsInRoom(uint roomEntityId)
        {
            var result = new List<uint>();
            foreach (var (entityId, _) in _entityService.GetAllComponents<ItemDataComponent>())
            {
                if (_entityService.TryGet<LocationComponent>(entityId, out var loc) &&
                    loc.RoomEntityId == roomEntityId)
                    result.Add(entityId);
            }
            return result;
        }

        public bool TryFindItemInRoom(uint roomEntityId, string token, out uint itemEntityId)
        {
            foreach (var entityId in GetItemsInRoom(roomEntityId))
            {
                if (!_entityService.TryGet<ItemDataComponent>(entityId, out var data)) continue;

                if (PrefixMatches(data.Name, token))
                {
                    itemEntityId = entityId;
                    return true;
                }
                foreach (var keyword in data.Keywords)
                {
                    if (PrefixMatches(keyword, token))
                    {
                        itemEntityId = entityId;
                        return true;
                    }
                }
            }
            itemEntityId = default;
            return false;
        }

        private static bool PrefixMatches(string candidate, string token) =>
            candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }
}
