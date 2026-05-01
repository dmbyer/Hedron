using Hedron.Core.Sessions;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Identity and live-connection data for a player entity.
    /// Not persisted — session ref is transient; display name is re-established on login.
    /// </summary>
    public class PlayerComponent : IComponent
    {
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Live connection for this player. Null if the player has disconnected.</summary>
        public ISession? Session { get; set; }
    }
}
