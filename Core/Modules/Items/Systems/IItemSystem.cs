using System.Collections.Generic;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Query and mutation operations on item entities. Pure ECS mutations only —
    /// no event publication, no persistence calls. Those are the command's responsibility.
    /// </summary>
    public interface IItemSystem
    {
        /// <summary>Returns entity ids of all items currently in <paramref name="roomEntityId"/>.</summary>
        IReadOnlyList<uint> GetItemsInRoom(uint roomEntityId);

        /// <summary>
        /// Finds the first item in <paramref name="roomEntityId"/> whose name or any keyword
        /// prefix-matches <paramref name="token"/>. Returns <c>false</c> if no match.
        /// </summary>
        bool TryFindItemInRoom(uint roomEntityId, string token, out uint itemEntityId);
    }
}
