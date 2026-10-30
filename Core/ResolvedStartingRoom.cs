namespace Hedron.Core
{
    /// <summary>Which step of the starting-room resolution chain produced a <see cref="ResolvedStartingRoom"/>.</summary>
    public enum StartingRoomSource
    {
        /// <summary>The world's <c>world.yaml</c> manifest declared a live room.</summary>
        Manifest,

        /// <summary>The operator's <c>World:StartingRoomBlueprintId</c> named a live room.</summary>
        Configuration,

        /// <summary>Neither resolved; the <c>room.void</c> room was used.</summary>
        VoidFallback,

        /// <summary>No void room either; the ordinal-least live room id was used.</summary>
        AnyRoomFallback,

        /// <summary>No live rooms at all; entity id is 0.</summary>
        Unresolved,
    }

    /// <summary>
    /// One immutable resolution of the world's starting room, published as a single reference on
    /// <see cref="WorldConfiguration.StartingRoom"/>. Readers take one snapshot per operation and use
    /// it for both ids, so an entity id is never paired with a blueprint id from another resolution.
    /// </summary>
    /// <param name="EntityId">Live room entity id, or 0 when <see cref="Source"/> is <see cref="StartingRoomSource.Unresolved"/>.</param>
    /// <param name="BlueprintId">Room blueprint id, or <c>null</c> when unresolved.</param>
    /// <param name="Source">Which resolution step produced the room.</param>
    /// <param name="DeclaredId">The manifest's declared <c>startingRoomId</c>, if any — kept even when it did not resolve.</param>
    public sealed record ResolvedStartingRoom(
        uint EntityId,
        string? BlueprintId,
        StartingRoomSource Source,
        string? DeclaredId)
    {
        /// <summary>The snapshot before any resolution has run.</summary>
        public static readonly ResolvedStartingRoom Unresolved =
            new(0u, null, StartingRoomSource.Unresolved, null);
    }
}
