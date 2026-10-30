using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Editor-side authoring of the world's <c>world.yaml</c> manifest. Implemented by
    /// <see cref="ContentDefinitionCatalog"/> so manifest writes serialize with every other catalog
    /// write (its write gate) and the rename cascade. File-only: a saved manifest takes effect on
    /// <c>reload</c> or restart, never by touching the live world (INV-8, INV-22/23).
    /// </summary>
    public interface IWorldManifestAuthoring
    {
        /// <summary>The on-disk manifest plus the resolution it produces over the authored rooms.</summary>
        WorldManifestView Load();

        /// <summary>The resolution <paramref name="draft"/> would produce, without writing anything.</summary>
        WorldManifestView Preview(WorldManifest draft);

        /// <summary>
        /// Saves the manifest under the catalog write gate. Refuses a malformed room id (format
        /// errors) and refuses while the existing file is malformed (it is left untouched). An id
        /// that matches no authored room saves with a warning. A null <c>StartingRoomId</c> writes
        /// a manifest with no declaration, so the config fallback applies.
        /// </summary>
        Task<ContentWriteResult> SaveAsync(WorldManifest manifest, CancellationToken ct = default);
    }
}
