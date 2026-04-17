using Hedron.Core.System;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Component containing player-specific configuration data
    /// </summary>
    public class PlayerConfigurationComponent : IComponent
    {
        /// <summary>
        /// Player's command prompt
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// Player configuration settings
        /// </summary>
        public PlayerConfiguration Configuration { get; set; } = new PlayerConfiguration();
    }
}