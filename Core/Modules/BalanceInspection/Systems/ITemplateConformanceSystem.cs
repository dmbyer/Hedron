using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    /// <summary>
    /// Domain-tier (BalanceInspection) fitter (sim-5): scales a template's existing stat vector
    /// (ratio-preserving, uniform) until its projected power lands in
    /// <see cref="IPowerBudgetSystem"/>'s <c>TargetRange</c> for its authored (Tier, Band) — the
    /// correction half of the observation→correction loop <see cref="IBalanceAuditSystem"/> opens.
    /// Scaling toward a target, never stat-block synthesis: an existing authored vector is
    /// multiplied by one factor, never invented from a range. Preview and apply both re-derive the
    /// fit from disk via <see cref="Authoring.Systems.IContentDefinitionCatalog"/> — never trusts a
    /// stale preview object. Returns records only; publishes nothing (INV-5) — writes are YAML-side
    /// via <c>SaveAsync</c>, never a live-entity or bus-facing mutation.
    /// </summary>
    public interface ITemplateConformanceSystem
    {
        /// <summary>
        /// Computes (without writing) the fit for one template, re-loaded from disk. Returns
        /// <c>AlreadyInRange</c> if the disk-current template already classifies within its
        /// authored (Tier, Band) target range, <c>NotFittable</c> with a reason if it cannot be
        /// scaled into range, or <c>Fitted</c> with the field-by-field diff.
        /// </summary>
        ConformancePreview Preview(BalanceAuditKind kind, string blueprintId);

        /// <summary>Runs <see cref="Preview"/> over every template <see cref="IBalanceAuditSystem.Audit"/> flags.</summary>
        IReadOnlyList<ConformancePreview> PreviewFlagged();

        /// <summary>
        /// Re-derives the fit from disk (idempotent — never trusts a prior <see cref="Preview"/>
        /// call) and, when a fit is found, writes it via <c>IContentDefinitionCatalog.SaveAsync</c>
        /// (validate-then-write, warn-but-allow). Performs no write for <c>AlreadyInRange</c> or
        /// <c>NotFittable</c>.
        /// </summary>
        Task<ConformanceApplyResult> ApplyAsync(BalanceAuditKind kind, string blueprintId, CancellationToken ct = default);

        /// <summary>
        /// Loops <see cref="ApplyAsync"/> over every template <see cref="IBalanceAuditSystem.Audit"/>
        /// flags — one code path shared with the single-template apply (INV-19).
        /// </summary>
        Task<ConformanceBulkResult> ApplyFlaggedAsync(CancellationToken ct = default);
    }
}
