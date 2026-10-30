using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// File store for the world's <c>world.yaml</c> manifest, rooted at <c>World:ContentDirectory</c>.
    /// Stateless: every <see cref="Read"/> re-reads the file.
    /// </summary>
    public interface IWorldManifestStore
    {
        /// <summary>Absolute or content-relative path of <c>world.yaml</c>.</summary>
        string FilePath { get; }

        /// <summary>
        /// Reads the manifest. A missing file yields <see cref="WorldManifest.Empty"/> with
        /// <c>Exists = false</c>; a malformed file yields <see cref="WorldManifest.Empty"/> with
        /// <c>Exists = true</c> and a non-null <c>Error</c>. Never throws for file content.
        /// Unknown keys are ignored.
        /// </summary>
        WorldManifestReadResult Read();

        /// <summary>
        /// Writes the manifest atomically (camelCase, null fields omitted). Unknown keys and YAML
        /// comments in the previous file are not preserved.
        /// </summary>
        Task WriteAsync(WorldManifest manifest, CancellationToken ct = default);

        /// <summary>
        /// The one read-modify-write path for every manifest writer: reads the file, <b>refuses</b>
        /// (writing nothing) while it is malformed, applies <paramref name="change"/>, defaults a
        /// missing <c>schemaVersion</c> to <see cref="WorldManifest.CurrentSchemaVersion"/>, and writes.
        /// A missing file is treated as <see cref="WorldManifest.Empty"/>.
        /// </summary>
        Task<WorldManifestUpdateResult> UpdateAsync(
            Func<WorldManifest, WorldManifest> change, CancellationToken ct = default);
    }

    /// <summary>Result of <see cref="IWorldManifestStore.Read"/>.</summary>
    public sealed record WorldManifestReadResult(WorldManifest Manifest, bool Exists, string? Error);

    /// <summary>
    /// Result of <see cref="IWorldManifestStore.UpdateAsync"/>: on success the manifest as written;
    /// on refusal (malformed existing file) the parse <see cref="Error"/> and no write.
    /// </summary>
    public sealed record WorldManifestUpdateResult(bool Success, WorldManifest? Written, string? Error);
}
