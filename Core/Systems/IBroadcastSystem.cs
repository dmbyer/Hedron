using System.Threading.Tasks;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Sends text output to one player or to every player occupying a room.
    /// </summary>
    public interface IBroadcastSystem
    {
        /// <summary>Sends a line to a single player. No-op if the player has no active session.</summary>
        Task SendToPlayerAsync(uint playerEntityId, string message);

        /// <summary>
        /// Sends a line to every player in <paramref name="roomEntityId"/>, optionally skipping one entity.
        /// </summary>
        Task SendToRoomAsync(uint roomEntityId, string message, uint? excludeEntityId = null);

        /// <summary>
        /// Sends the full room description (name, description, exits, occupants) to a player.
        /// Used by <c>look</c>, movement arrival, and on-connect placement.
        /// </summary>
        Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId);
    }
}
