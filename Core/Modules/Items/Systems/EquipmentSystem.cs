using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Items.Systems
{
    public sealed class EquipmentSystem : IEquipmentSystem
    {
        private readonly EntityService _entityService;

        public EquipmentSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public IReadOnlyList<WornSlot> GetWornSlots(uint itemEntityId)
        {
            if (_entityService.TryGet<ItemDataComponent>(itemEntityId, out var data) &&
                data.WornSlots is { Count: > 0 })
                return data.WornSlots;
            return Array.Empty<WornSlot>();
        }

        public IReadOnlyList<uint> GetEquippedItems(uint characterEntityId)
        {
            if (!_entityService.TryGet<EquipmentComponent>(characterEntityId, out var eq))
                return Array.Empty<uint>();
            return new List<uint>(eq.Slots.Values);
        }

        public bool TryFindEquippedItem(uint characterEntityId, string token, out uint itemEntityId)
        {
            foreach (var id in GetEquippedItems(characterEntityId))
            {
                if (!_entityService.TryGet<ItemDataComponent>(id, out var data)) continue;

                if (PrefixMatches(data.Name, token))
                {
                    itemEntityId = id;
                    return true;
                }
                foreach (var keyword in data.Keywords)
                {
                    if (PrefixMatches(keyword, token))
                    {
                        itemEntityId = id;
                        return true;
                    }
                }
            }
            itemEntityId = default;
            return false;
        }

        public void EquipItem(uint characterEntityId, uint itemEntityId)
        {
            var slots = GetWornSlots(itemEntityId);
            if (slots.Count == 0) return;

            // Implicitly remove any item already in the target slots.
            foreach (var slot in slots)
                RemoveFromSlot(characterEntityId, slot);

            // Move out of inventory.
            if (_entityService.TryGet<InventoryComponent>(characterEntityId, out var inv))
                inv.ItemEntityIds.Remove(itemEntityId);

            // Place in all declared slots.
            if (_entityService.TryGet<EquipmentComponent>(characterEntityId, out var eq))
            {
                foreach (var slot in slots)
                    eq.Slots[slot] = itemEntityId;
            }
        }

        public void RemoveItem(uint characterEntityId, uint itemEntityId)
        {
            if (!_entityService.TryGet<EquipmentComponent>(characterEntityId, out var eq))
                return;

            var slotsToRemove = new List<WornSlot>();
            foreach (var (slot, id) in eq.Slots)
            {
                if (id == itemEntityId)
                    slotsToRemove.Add(slot);
            }
            foreach (var slot in slotsToRemove)
                eq.Slots.Remove(slot);

            if (_entityService.TryGet<InventoryComponent>(characterEntityId, out var inv))
                inv.ItemEntityIds.Add(itemEntityId);
        }

        public void RemoveFromSlot(uint characterEntityId, WornSlot slot)
        {
            if (!_entityService.TryGet<EquipmentComponent>(characterEntityId, out var eq))
                return;
            if (!eq.Slots.TryGetValue(slot, out var existingId))
                return;

            eq.Slots.Remove(slot);

            // If the displaced item doesn't occupy any other slot, move it back to inventory.
            if (!eq.Slots.ContainsValue(existingId))
            {
                if (_entityService.TryGet<InventoryComponent>(characterEntityId, out var inv))
                    inv.ItemEntityIds.Add(existingId);
            }
        }

        private static bool PrefixMatches(string candidate, string token) =>
            candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }
}
