namespace Hedron.Core
{
    /// <summary>
    /// Runtime configuration set by the world bootstrap before the host starts accepting connections.
    /// </summary>
    public class WorldConfiguration
    {
        /// <summary>Entity id of the room new players are placed in on connect.</summary>
        public uint StartingRoomEntityId { get; set; }

        /// <summary>
        /// Blueprint id of the starting room. Populated alongside <see cref="StartingRoomEntityId"/>
        /// by <c>WorldContentLoader.ResolveStartingRoom</c>. Used as a cross-restart-stable
        /// fallback reference (e.g. for <c>RespawnComponent</c> at character creation).
        /// </summary>
        public string? StartingRoomBlueprintId { get; set; }
    }
}
