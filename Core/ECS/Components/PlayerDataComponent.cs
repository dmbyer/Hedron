using Hedron.Core.Commands;
using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing player-specific data
    /// </summary>
    public class PlayerDataComponent : IComponent
    {
        /// <summary>
        /// The network connection ID
        /// </summary>
        public string ConnectionID { get; set; }

        /// <summary>
        /// The entity's IO queues for network communication
        /// </summary>
        public IOHandler IOHandler { get; set; }

        /// <summary>
        /// The entity's privilege level
        /// </summary>
        public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.NPC;

        /// <summary>
        /// Entity state
        /// </summary>
        public EntityState State { get; set; } = EntityState.Active;
    }
}