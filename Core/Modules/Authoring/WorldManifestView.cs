using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Editor view of the world's <c>world.yaml</c>, built by <see cref="Systems.IWorldManifestAuthoring"/>.
    /// </summary>
    /// <param name="Manifest">The manifest (the on-disk one for <c>Load</c>, the draft for <c>Preview</c>).</param>
    /// <param name="FilePath">Where <c>world.yaml</c> lives.</param>
    /// <param name="Exists">Whether the file exists on disk.</param>
    /// <param name="ReadError">Parse error of the on-disk file, if malformed — saves are refused until it is fixed by hand.</param>
    /// <param name="ConfigFallbackId">The operator's <c>World:StartingRoomBlueprintId</c>.</param>
    /// <param name="Preview">The resolution the manifest would produce over the authored rooms.</param>
    public sealed record WorldManifestView(
        WorldManifest Manifest,
        string FilePath,
        bool Exists,
        string? ReadError,
        string ConfigFallbackId,
        StartingRoomDecision Preview);
}
