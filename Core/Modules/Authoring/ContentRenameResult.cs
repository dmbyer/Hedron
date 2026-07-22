using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Outcome of <see cref="Systems.IContentDefinitionCatalog.RenameAsync"/>. Modeled on
    /// <see cref="ContentDeleteResult"/> (the cascade edits) fused with <see cref="ContentWriteResult"/>
    /// (success/errors/warnings, because a rename can be refused). On failure, <see cref="Success"/>
    /// is false, <see cref="Errors"/> lists the reasons, and neither the new file was written nor
    /// the old file was touched. On success, <see cref="OldPath"/> was deleted, <see cref="NewPath"/>
    /// carries the renamed definition's full state, <see cref="CascadeEdits"/> enumerates every
    /// external referrer rewritten <c>oldId → newId</c>, and <see cref="Warnings"/> carries
    /// non-blocking advisories (kind-prefix mismatch, out-of-YAML re-key notices).
    /// </summary>
    /// <remarks>
    /// YAML-file operation only — no <c>EntityService</c> mutation, no SQLite write, no live-world
    /// change (INV-22/23). Applying the rename to the live world remains a separate <c>reload</c>.
    /// </remarks>
    public sealed record ContentRenameResult(
        bool Success,
        string OldPath,
        string NewPath,
        string OldBlueprintId,
        string NewBlueprintId,
        IReadOnlyList<ReferrerEdit> CascadeEdits,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors)
    {
        public static ContentRenameResult Ok(
            string oldPath,
            string newPath,
            string oldBlueprintId,
            string newBlueprintId,
            IReadOnlyList<ReferrerEdit> cascadeEdits,
            IReadOnlyList<string> warnings) =>
            new(true, oldPath, newPath, oldBlueprintId, newBlueprintId, cascadeEdits, warnings, Array.Empty<string>());

        public static ContentRenameResult Failed(
            string oldBlueprintId,
            string newBlueprintId,
            IReadOnlyList<string> errors) =>
            new(false, string.Empty, string.Empty, oldBlueprintId, newBlueprintId,
                Array.Empty<ReferrerEdit>(), Array.Empty<string>(), errors);
    }
}
