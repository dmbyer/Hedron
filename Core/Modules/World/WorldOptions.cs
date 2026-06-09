namespace Hedron.Core.Modules.World
{
    /// <summary>
    /// Settings bound from the <c>World:</c> configuration section.
    /// Override via environment variable: <c>HEDRON_World__ContentDirectory</c>,
    /// <c>HEDRON_World__StartingRoomBlueprintId</c>.
    /// </summary>
    public sealed class WorldOptions
    {
        /// <summary>
        /// Root directory for authored YAML content (rooms, items, mobs, areas).
        /// May be an absolute path or relative to the working directory.
        /// Default: <c>data/content/</c>.
        /// </summary>
        public string ContentDirectory { get; set; } = "data/content/";

        /// <summary>
        /// Blueprint ID of the room players are placed in when their saved room cannot be found.
        /// Default: <c>room.crossroads</c>.
        /// </summary>
        public string StartingRoomBlueprintId { get; set; } = "room.crossroads";
    }
}
