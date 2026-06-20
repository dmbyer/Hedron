using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Outcome of <see cref="Systems.IContentDefinitionCatalog.SaveAsync"/>. On validation
    /// failure, <see cref="Success"/> is false, <see cref="Errors"/> lists the reasons, and
    /// no file is written (validation-before-write). On structural pass, <see cref="Success"/>
    /// is true, the file is written, and <see cref="Warnings"/> carries any non-blocking
    /// cross-reference notices (e.g. a dangling <c>AreaId</c> that does not resolve on disk).
    /// Warnings are Authoring-owned — they do not live in <c>ValidationReport</c>
    /// (World-owned, structural-only).
    /// </summary>
    public sealed record ContentWriteResult(
        bool Success,
        string BlueprintId,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public static ContentWriteResult Ok(string blueprintId) =>
            new(true, blueprintId, Array.Empty<string>(), Array.Empty<string>());

        public static ContentWriteResult OkWithWarnings(string blueprintId, IReadOnlyList<string> warnings) =>
            new(true, blueprintId, Array.Empty<string>(), warnings);

        public static ContentWriteResult Failed(string blueprintId, IReadOnlyList<string> errors) =>
            new(false, blueprintId, errors, Array.Empty<string>());
    }
}
