using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Structured result of a content-validation pass. Never thrown — callers decide whether a
    /// non-empty <see cref="Errors"/> list is fatal (the boot bootstrap throws; the editor renders
    /// the errors inline). A report with no errors is <see cref="IsValid"/> — <see cref="Warnings"/>
    /// never affects <see cref="IsValid"/>; a warn-not-error rule (e.g. coordinate collisions)
    /// surfaces there instead so callers can log/display it without aborting.
    /// </summary>
    public sealed class ValidationReport
    {
        public static ValidationReport Ok { get; } = new ValidationReport(Array.Empty<string>());

        public IReadOnlyList<string> Errors { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool IsValid => Errors.Count == 0;

        public ValidationReport(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
        {
            Errors = errors;
            Warnings = warnings ?? Array.Empty<string>();
        }
    }
}
