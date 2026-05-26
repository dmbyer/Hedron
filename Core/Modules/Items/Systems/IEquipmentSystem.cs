using System.Collections.Generic;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Query and mutation operations on character equipment slots. Pure ECS mutations only —
    /// no event publication, no persistence calls. Those are the command's responsibility.
    /// </summary>
    public interface IEquipmentSystem
    {
        /// <summary>Returns the slots declared on <paramref name="itemEntityId"/>'s <c>ItemDataComponent</c>. Empty list means not wearable.</summary>
        IReadOnlyList<WornSlot> GetWornSlots(uint itemEntityId);

        /// <summary>Returns entity ids of all items currently equipped by <paramref name="characterEntityId"/>.</summary>
        IReadOnlyList<uint> GetEquippedItems(uint characterEntityId);

        /// <summary>
        /// Finds a worn item on <paramref name="characterEntityId"/> whose name or keyword
        /// prefix-matches <paramref name="token"/>. Returns <c>false</c> if no match.
        /// </summary>
        bool TryFindEquippedItem(uint characterEntityId, string token, out uint itemEntityId);

        /// <summary>
        /// Moves <paramref name="itemEntityId"/> from <paramref name="characterEntityId"/>'s inventory
        /// into their equipment slots. Internally clears any occupied slot before placing the item
        /// (the displaced item moves back to inventory silently). Assumes the item has at least one
        /// declared <see cref="WornSlot"/>.
        /// </summary>
        void EquipItem(uint characterEntityId, uint itemEntityId);

        /// <summary>
        /// Moves <paramref name="itemEntityId"/> from equipment slots back to
        /// <paramref name="characterEntityId"/>'s inventory.
        /// </summary>
        void RemoveItem(uint characterEntityId, uint itemEntityId);

        /// <summary>
        /// Silently moves whatever item occupies <paramref name="slot"/> on
        /// <paramref name="characterEntityId"/> back to their inventory. No-op if the slot is empty.
        /// Used internally by <see cref="EquipItem"/> and exposed for callers who need per-slot control.
        /// </summary>
        void RemoveFromSlot(uint characterEntityId, WornSlot slot);
    }
}
