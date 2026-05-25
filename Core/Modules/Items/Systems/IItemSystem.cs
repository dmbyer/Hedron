using System.Collections.Generic;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Query and mutation operations on item entities. Pure ECS mutations only —
    /// no event publication, no persistence calls. Those are the command's responsibility.
    /// </summary>
    public interface IItemSystem
    {
        /// <summary>Returns entity ids of all items currently on the ground in <paramref name="roomEntityId"/>.</summary>
        IReadOnlyList<uint> GetItemsInRoom(uint roomEntityId);

        /// <summary>Returns entity ids of all items in <paramref name="holderEntityId"/>'s inventory.</summary>
        IReadOnlyList<uint> GetItemsInInventory(uint holderEntityId);

        /// <summary>
        /// Finds the first item in <paramref name="roomEntityId"/> whose name or any keyword
        /// prefix-matches <paramref name="token"/>. Returns <c>false</c> if no match.
        /// </summary>
        bool TryFindItemInRoom(uint roomEntityId, string token, out uint itemEntityId);

        /// <summary>
        /// Finds the first item in <paramref name="holderEntityId"/>'s inventory whose name or
        /// any keyword prefix-matches <paramref name="token"/>. Returns <c>false</c> if no match.
        /// </summary>
        bool TryFindItemInInventory(uint holderEntityId, string token, out uint itemEntityId);

        /// <summary>
        /// Moves <paramref name="itemEntityId"/> from the ground into <paramref name="holderEntityId"/>'s
        /// inventory: removes <c>LocationComponent</c> from the item and appends to
        /// <c>InventoryComponent.ItemEntityIds</c>. No-ops silently if the item has no
        /// <c>LocationComponent</c> (already picked up — acceptable race condition).
        /// </summary>
        void MoveToInventory(uint itemEntityId, uint holderEntityId);

        /// <summary>
        /// Moves <paramref name="itemEntityId"/> from <paramref name="holderEntityId"/>'s inventory
        /// to the ground in <paramref name="roomEntityId"/>: removes the item id from
        /// <c>InventoryComponent.ItemEntityIds</c> and attaches a <c>LocationComponent</c>.
        /// </summary>
        void DropToRoom(uint itemEntityId, uint holderEntityId, uint roomEntityId);
    }
}
