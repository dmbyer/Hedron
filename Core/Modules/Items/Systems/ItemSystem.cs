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

        public IReadOnlyList<uint> GetItemsInInventory(uint holderEntityId)
        {
            if (!_entityService.TryGet<InventoryComponent>(holderEntityId, out var inv))
                return Array.Empty<uint>();
            return inv.ItemEntityIds.AsReadOnly();
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

        public bool TryFindItemInInventory(uint holderEntityId, string token, out uint itemEntityId)
        {
            foreach (var entityId in GetItemsInInventory(holderEntityId))
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

        public void MoveToInventory(uint itemEntityId, uint holderEntityId)
        {
            if (!_entityService.HasComponent<LocationComponent>(itemEntityId))
                return; // already picked up — race condition, silently no-op

            _entityService.RemoveComponent<LocationComponent>(itemEntityId);
            // Decouple from blueprint slot (INV-21): clearing BlueprintComponent lets the
            // template re-spawn a fresh instance in the spawn room on next restart.
            _entityService.RemoveComponent<BlueprintComponent>(itemEntityId);

            if (_entityService.TryGet<InventoryComponent>(holderEntityId, out var inv))
                inv.ItemEntityIds.Add(itemEntityId);
        }

        public void DropToRoom(uint itemEntityId, uint holderEntityId, uint roomEntityId)
        {
            if (_entityService.TryGet<InventoryComponent>(holderEntityId, out var inv))
                inv.ItemEntityIds.Remove(itemEntityId);

            var blueprintId = _entityService.TryGet<BlueprintComponent>(roomEntityId, out var bp)
                ? bp.BlueprintId
                : null;
            _entityService.AddComponent(itemEntityId, new LocationComponent
            {
                RoomEntityId = roomEntityId,
                RoomBlueprintId = blueprintId,
            });
        }

        private static bool PrefixMatches(string candidate, string token) =>
            candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }
}
