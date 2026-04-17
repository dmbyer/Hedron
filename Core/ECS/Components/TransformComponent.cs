using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing spatial and hierarchical relationship information
    /// </summary>
    public class TransformComponent : IComponent
    {
        /// <summary>
        /// The parent entity ID for instance objects
        /// </summary>
        public uint? InstanceParent { get; set; }

        /// <summary>
        /// The parent entity IDs for prototype objects
        /// </summary>
        public List<uint> PrototypeParents { get; set; } = new List<uint>();

        /// <summary>
        /// Child entity IDs for containers
        /// </summary>
        public List<uint> ChildrenIds { get; set; } = new List<uint>();

        /// <summary>
        /// Current room ID for spatial positioning
        /// </summary>
        public uint? RoomId { get; set; }

        /// <summary>
        /// Current area ID for spatial positioning
        /// </summary>
        public uint? AreaId { get; set; }
    }
}