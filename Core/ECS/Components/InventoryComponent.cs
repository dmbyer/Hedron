using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing inventory item storage (unequipped items carried by entity)
    /// Equipment is handled separately by EquipmentComponent
    /// </summary>
    public class InventoryComponent : IComponent
    {
        /// <summary>
        /// List of entity IDs representing items in the inventory
        /// </summary>
        public List<uint> Items { get; set; } = new List<uint>();

        /// <summary>
        /// Maximum number of items that can be carried (-1 for unlimited)
        /// </summary>
        public int Capacity { get; set; } = -1;

        /// <summary>
        /// Current number of items in inventory
        /// </summary>
        public int Count => Items.Count;

        /// <summary>
        /// Adds an item to the inventory
        /// </summary>
        /// <param name="itemId">The item entity ID to add</param>
        /// <returns>True if item was added, false if inventory is full</returns>
        public bool AddItem(uint itemId)
        {
            if (Capacity > 0 && Items.Count >= Capacity)
                return false;

            if (!Items.Contains(itemId))
            {
                Items.Add(itemId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes an item from the inventory
        /// </summary>
        /// <param name="itemId">The item entity ID to remove</param>
        /// <returns>True if item was removed</returns>
        public bool RemoveItem(uint itemId)
        {
            return Items.Remove(itemId);
        }

        /// <summary>
        /// Checks if the inventory contains a specific item
        /// </summary>
        /// <param name="itemId">The item entity ID to check for</param>
        /// <returns>True if item is in inventory</returns>
        public bool ContainsItem(uint itemId)
        {
            return Items.Contains(itemId);
        }

        /// <summary>
        /// Gets all items in the inventory
        /// </summary>
        /// <returns>List of item entity IDs</returns>
        public List<uint> GetAllItems()
        {
            return new List<uint>(Items);
        }

        /// <summary>
        /// Checks if inventory is full
        /// </summary>
        /// <returns>True if at capacity</returns>
        public bool IsFull()
        {
            return Capacity > 0 && Items.Count >= Capacity;
        }

        /// <summary>
        /// Checks if inventory is empty
        /// </summary>
        /// <returns>True if no items</returns>
        public bool IsEmpty()
        {
            return Items.Count == 0;
        }

        /// <summary>
        /// Clears all items from inventory
        /// </summary>
        /// <returns>List of removed item IDs</returns>
        public List<uint> ClearAll()
        {
            var removedItems = new List<uint>(Items);
            Items.Clear();
            return removedItems;
        }
    }
}