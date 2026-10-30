using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>
    /// Published after an admin declares the world's starting room via <c>startroom here</c>.
    /// <paramref name="PreviousStartingRoomId"/> is the previous <b>resolved</b> starting room's
    /// blueprint id (which may have come from config or a fallback), not only a prior manifest value.
    /// </summary>
    public record StartingRoomSetByAdminEvent(
        uint AdminEntityId,
        uint RoomEntityId,
        string RoomBlueprintId,
        string? PreviousStartingRoomId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
