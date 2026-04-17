using Core.ECS.Properties;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing equipped item data organized by equipment slots
    /// </summary>
    public class EquipmentComponent : IComponent
    {
        /// <summary>
        /// Dictionary mapping equipment slots to equipped entity IDs
        /// </summary>
        public Dictionary<ItemSlot, uint?> EquippedItems { get; set; } = new Dictionary<ItemSlot, uint?>();

        /// <summary>
        /// Initializes the equipment slots
        /// </summary>
        public EquipmentComponent()
        {
            InitializeSlots();
        }

        /// <summary>
        /// Initializes all equipment slots to null (empty)
        /// </summary>
        private void InitializeSlots()
        {
            // Initialize all equipment slots except None
            EquippedItems[ItemSlot.Light] = null;
            EquippedItems[ItemSlot.Orbit] = null;
            EquippedItems[ItemSlot.Head] = null;
            EquippedItems[ItemSlot.Neck] = null;
            EquippedItems[ItemSlot.Torso] = null;
            EquippedItems[ItemSlot.Arms] = null;
            EquippedItems[ItemSlot.Hands] = null;
            EquippedItems[ItemSlot.Waist] = null;
            EquippedItems[ItemSlot.Legs] = null;
            EquippedItems[ItemSlot.Feet] = null;
            EquippedItems[ItemSlot.Finger] = null;
            EquippedItems[ItemSlot.OneHandedWeapon] = null;
            EquippedItems[ItemSlot.TwoHandedWeapon] = null;
            EquippedItems[ItemSlot.Shield] = null;
        }

        /// <summary>
        /// Equips an item in the specified slot
        /// </summary>
        /// <param name="slot">The equipment slot</param>
        /// <param name="itemId">The item entity ID to equip</param>
        /// <returns>The ID of the previously equipped item, if any</returns>
        public uint? EquipItem(ItemSlot slot, uint itemId)
        {
            if (slot == ItemSlot.None)
                return null;

            var previousItem = EquippedItems.GetValueOrDefault(slot);
            EquippedItems[slot] = itemId;
            return previousItem;
        }

        /// <summary>
        /// Unequips an item from the specified slot
        /// </summary>
        /// <param name="slot">The equipment slot to clear</param>
        /// <returns>The ID of the unequipped item, if any</returns>
        public uint? UnequipItem(ItemSlot slot)
        {
            if (slot == ItemSlot.None || !EquippedItems.ContainsKey(slot))
                return null;

            var equippedItem = EquippedItems[slot];
            EquippedItems[slot] = null;
            return equippedItem;
        }

        /// <summary>
        /// Gets the item equipped in the specified slot
        /// </summary>
        /// <param name="slot">The equipment slot</param>
        /// <returns>The equipped item ID, or null if slot is empty</returns>
        public uint? GetEquippedItem(ItemSlot slot)
        {
            return EquippedItems.GetValueOrDefault(slot);
        }

        /// <summary>
        /// Checks if the specified slot has an item equipped
        /// </summary>
        /// <param name="slot">The equipment slot</param>
        /// <returns>True if an item is equipped in the slot</returns>
        public bool IsSlotEquipped(ItemSlot slot)
        {
            return EquippedItems.GetValueOrDefault(slot) != null;
        }

        /// <summary>
        /// Gets all equipped items with their slots
        /// </summary>
        /// <returns>Dictionary of equipped items by slot</returns>
        public Dictionary<ItemSlot, uint> GetAllEquippedItems()
        {
            var equipped = new Dictionary<ItemSlot, uint>();
            foreach (var kvp in EquippedItems)
            {
                if (kvp.Value.HasValue)
                {
                    equipped[kvp.Key] = kvp.Value.Value;
                }
            }
            return equipped;
        }

        /// <summary>
        /// Clears all equipped items
        /// </summary>
        /// <returns>List of entity IDs that were unequipped</returns>
        public List<uint> UnequipAll()
        {
            var unequippedItems = new List<uint>();
            foreach (var slot in EquippedItems.Keys.ToList())
            {
                var itemId = UnequipItem(slot);
                if (itemId.HasValue)
                {
                    unequippedItems.Add(itemId.Value);
                }
            }
            return unequippedItems;
        }
    }
}