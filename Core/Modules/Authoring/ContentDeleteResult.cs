using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Outcome of <see cref="Systems.IContentDefinitionCatalog.DeleteAsync"/>. Carries the path
    /// of the deleted YAML file and the per-referrer cascade edits applied to clear dangling links
    /// before the delete. Pure data — no policy, no event bus.
    /// </summary>
    /// <remarks>
    /// The delete operation is YAML-file-only: <c>File.Delete</c> + writer rewrites. It never
    /// calls <c>EntityService.DestroyEntity</c>, issues a SQLite delete, or mutates the live world
    /// (INV-22/23). Applying the deletion to the live world remains a separate <c>reload</c>.
    /// </remarks>
    /// <param name="DeletedPath">Absolute path of the YAML file that was deleted.</param>
    /// <param name="DeletedBlueprintId">Blueprint id of the deleted definition.</param>
    /// <param name="CascadeEdits">
    /// Each referrer that was rewritten to drop the now-dangling link — one entry per edit.
    /// </param>
    public sealed record ContentDeleteResult(
        string DeletedPath,
        string DeletedBlueprintId,
        IReadOnlyList<ReferrerEdit> CascadeEdits);
}
