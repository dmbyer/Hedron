using System;
using System.Threading.Tasks;
using Hedron.Core.Output;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Delivers typed <see cref="IOutputMessage"/> output to one player, a room, or every session.
    /// Each recipient's message is rendered by their transport's <c>IOutputFormatter</c> so callers
    /// never touch raw strings or transport-encoding.
    /// </summary>
    public interface IBroadcastSystem
    {
        /// <summary>
        /// Sends <paramref name="message"/> to every player in <paramref name="roomEntityId"/>
        /// whose entity id satisfies <paramref name="audienceFilter"/> (or all players if
        /// <paramref name="audienceFilter"/> is <c>null</c>).
        /// </summary>
        Task SendToRoomAsync(
            uint roomEntityId,
            IOutputMessage message,
            Func<uint, bool>? audienceFilter = null);

        /// <summary>Sends <paramref name="message"/> to every registered session.</summary>
        Task SendToAllAsync(IOutputMessage message);

        /// <summary>
        /// Builds and sends a <c>RoomDescriptionMessage</c> to a single player.
        /// Used by <c>look</c>, movement arrival, and on-connect placement.
        /// </summary>
        Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId);
    }
}
