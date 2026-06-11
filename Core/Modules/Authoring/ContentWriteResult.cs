using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Outcome of <see cref="Systems.IContentDefinitionCatalog.SaveAsync"/>. On validation
    /// failure, <see cref="Success"/> is false, <see cref="Errors"/> lists the reasons, and
    /// no file is written (validation-before-write).
    /// </summary>
    public sealed record ContentWriteResult(bool Success, string BlueprintId, IReadOnlyList<string> Errors)
    {
        public static ContentWriteResult Ok(string blueprintId) =>
            new(true, blueprintId, Array.Empty<string>());

        public static ContentWriteResult Failed(string blueprintId, IReadOnlyList<string> errors) =>
            new(false, blueprintId, errors);
    }
}
