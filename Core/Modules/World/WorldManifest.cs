namespace Hedron.Core.Modules.World
{
    /// <summary>
    /// World-level facts declared by the optional <c>world.yaml</c> at the content root. A non-entity
    /// world definition — never a template kind, never spawned (INV-12), never SQLite (INV-23).
    /// </summary>
    /// <param name="SchemaVersion">Reserved; logged on mismatch, not enforced.</param>
    /// <param name="StartingRoomId">Room blueprint id where new characters land and fall back to on respawn.</param>
    public sealed record WorldManifest(int? SchemaVersion, string? StartingRoomId)
    {
        /// <summary>The schema version this engine reads and writes.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The manifest of a world with no (or an unreadable) <c>world.yaml</c>.</summary>
        public static readonly WorldManifest Empty = new(null, null);
    }
}
