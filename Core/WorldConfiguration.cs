namespace Hedron.Core
{
    /// <summary>
    /// Runtime configuration set by the world bootstrap before the host starts accepting connections.
    /// </summary>
    public class WorldConfiguration
    {
        /// <summary>Entity id of the room new players are placed in on connect.</summary>
        public uint StartingRoomEntityId { get; set; }
    }
}
