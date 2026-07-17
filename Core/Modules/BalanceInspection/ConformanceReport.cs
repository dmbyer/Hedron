using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>Outcome of a conformance fit attempt (<see cref="Systems.ITemplateConformanceSystem"/>).</summary>
    public enum ConformanceStatus
    {
        Fitted,
        AlreadyInRange,
        NotFittable,
    }

    /// <summary>Why a template could not be fitted. <see cref="None"/> outside <see cref="ConformanceStatus.NotFittable"/>.</summary>
    public enum ConformanceNotFittableReason
    {
        None,
        ZeroWeightedPowerVector,
        UnbandedTemplate,
        RoundingDidNotConverge,
    }

    /// <summary>One projection-read field the fitter scaled, before and after.</summary>
    public sealed record ConformanceFieldChange(ScoreId Field, int Before, int After);

    /// <summary>
    /// Result of <see cref="Systems.ITemplateConformanceSystem.Preview"/>: the field-by-field diff,
    /// power/cell before and after, and the status the Integrity page renders. Never applied — a
    /// pure preview of what <see cref="ConformanceApplyResult"/> would do.
    /// </summary>
    public sealed record ConformancePreview(
        BalanceAuditKind Kind,
        string BlueprintId,
        ConformanceStatus Status,
        ConformanceNotFittableReason NotFittableReason,
        int PowerBefore,
        int PowerAfter,
        PowerBand CellBefore,
        PowerBand CellAfter,
        IReadOnlyList<ConformanceFieldChange> FieldChanges);

    /// <summary>
    /// Result of <see cref="Systems.ITemplateConformanceSystem.ApplyAsync"/>: whether a write
    /// happened, the catalog's validation errors/warnings when it did, and why not when it didn't.
    /// </summary>
    public sealed record ConformanceApplyResult(
        BalanceAuditKind Kind,
        string BlueprintId,
        bool Success,
        ConformanceStatus Status,
        ConformanceNotFittableReason NotFittableReason,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public static ConformanceApplyResult AlreadyInRange(BalanceAuditKind kind, string blueprintId) =>
            new(kind, blueprintId, true, ConformanceStatus.AlreadyInRange, ConformanceNotFittableReason.None,
                Array.Empty<string>(), Array.Empty<string>());

        public static ConformanceApplyResult NotFittable(
            BalanceAuditKind kind, string blueprintId, ConformanceNotFittableReason reason) =>
            new(kind, blueprintId, false, ConformanceStatus.NotFittable, reason,
                Array.Empty<string>(), Array.Empty<string>());

        public static ConformanceApplyResult Fitted(
            BalanceAuditKind kind, string blueprintId, IReadOnlyList<string> warnings) =>
            new(kind, blueprintId, true, ConformanceStatus.Fitted, ConformanceNotFittableReason.None,
                Array.Empty<string>(), warnings);

        public static ConformanceApplyResult Failed(
            BalanceAuditKind kind, string blueprintId, IReadOnlyList<string> errors) =>
            new(kind, blueprintId, false, ConformanceStatus.Fitted, ConformanceNotFittableReason.None,
                errors, Array.Empty<string>());
    }

    /// <summary>
    /// Aggregate result of <see cref="Systems.ITemplateConformanceSystem.ApplyFlaggedAsync"/> — one
    /// <see cref="ConformanceApplyResult"/> per template in the flagged set, in the same order
    /// <see cref="Systems.IBalanceAuditSystem.Audit"/> reported them.
    /// </summary>
    public sealed record ConformanceBulkResult(IReadOnlyList<ConformanceApplyResult> Results);
}
