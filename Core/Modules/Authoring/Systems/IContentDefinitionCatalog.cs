using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// The single backing for offline content authoring: read / list / load / create / validate /
    /// write over the YAML content-definition families (area, room, item, mob). Both the offline
    /// Blazor editor and the headless bulk generator are thin callers of this facade — no authoring
    /// logic lives in a UI component or a generator trigger.
    /// </summary>
    /// <remarks>
    /// The catalog writes YAML only. It never creates a live entity, adds <c>PersistentEntity</c>,
    /// or calls <c>SaveEntityAsync</c> (INV-12/22/23) — applying content to the live world is a
    /// separate <c>reload</c> step. Per-kind specifics (which writer, which template) are dispatched
    /// inside the catalog by <see cref="ContentKind"/>.
    /// </remarks>
    public interface IContentDefinitionCatalog
    {
        /// <summary>Enumerates the definitions of <paramref name="kind"/> present on disk.</summary>
        IReadOnlyList<ContentSummary> List(ContentKind kind);

        /// <summary>Loads one definition by blueprint id, or <c>null</c> if no file exists.</summary>
        ContentDefinition? Load(ContentKind kind, string blueprintId);

        /// <summary>
        /// Validates then writes a definition to its YAML file. Refuses to write (and returns a
        /// failed result carrying the validation errors) when the definition is invalid.
        /// </summary>
        Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default);

        /// <summary>
        /// Produces a new, un-persisted definition with a freshly minted ad-hoc blueprint id and the
        /// given name. Creates no live entity and registers nothing — call <see cref="SaveAsync"/> to
        /// persist it.
        /// </summary>
        ContentDefinition CreateNew(ContentKind kind, string name);
    }
}
