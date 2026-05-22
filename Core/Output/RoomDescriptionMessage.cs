using System.Collections.Generic;
using Hedron.Core;

namespace Hedron.Core.Output
{
    /// <summary>Full room description: name, body text, exit map, and visible occupants.</summary>
    public sealed record RoomDescriptionMessage(
        uint RoomEntityId,
        string Name,
        string Description,
        IReadOnlyDictionary<Direction, string> Exits,
        IReadOnlyList<string> OccupantNames) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
