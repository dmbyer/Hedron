using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Static description and exit map for a room entity.
    /// Exits map a direction to the entity id of the connected room.
    /// </summary>
    public class RoomComponent : IComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Direction → destination room entity id.</summary>
        public Dictionary<Direction, uint> Exits { get; set; } = new();
    }
}
