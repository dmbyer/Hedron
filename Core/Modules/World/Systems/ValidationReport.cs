using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Structured result of a content-validation pass. Never thrown — callers decide whether a
    /// non-empty <see cref="Errors"/> list is fatal (the boot bootstrap throws; the editor renders
    /// the errors inline). A report with no errors is <see cref="IsValid"/>.
    /// </summary>
    public sealed class ValidationReport
    {
        public static ValidationReport Ok { get; } = new ValidationReport(Array.Empty<string>());

        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;

        public ValidationReport(IReadOnlyList<string> errors)
        {
            Errors = errors;
        }
    }
}
