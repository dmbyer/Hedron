using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// Result of <see cref="IBalanceStandardsStore.SaveAsync"/>: on structural-validation failure,
    /// <see cref="Success"/> is <c>false</c>, <see cref="Errors"/> is populated, and no file is
    /// written (refuse). On structural pass, the file is written and <see cref="Warnings"/> carries
    /// any mirror-drift or unknown-ability-kit notices (warn-but-allow). Mirrors
    /// <c>ContentWriteResult</c>'s refuse-vs-warn posture without its blueprint-id/kind fields —
    /// standards are a single document, not a per-blueprint family.
    /// </summary>
    public sealed record BalanceStandardsSaveResult(
        bool Success,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// Domain-tier (BalanceInspection) YAML load/validate/save for the balance-standards document.
    /// Deliberately outside <c>IContentDefinitionCatalog</c> (seed OQ1) — a single-document criteria
    /// file doesn't fit the catalog's per-blueprint <c>IEntityTemplate</c>/spawn/delete-cascade
    /// semantics — but mirrors its validate-then-write posture (structural failure refuses the
    /// write; drift warns but allows) and atomic-write discipline (tmp → rename).
    /// </summary>
    public interface IBalanceStandardsStore
    {
        /// <summary>
        /// Loads the configured standards file, or <see cref="BalanceStandardsDefaults.Document"/>
        /// if absent. Throws <see cref="System.InvalidOperationException"/> naming the file and
        /// violation on any structural failure (fail-fast at boot). Returns one warning per
        /// mirror-drifted field or unknown ability-kit id — never silently absorbed.
        /// </summary>
        (BalanceStandardsDocument Document, IReadOnlyList<string> Warnings) Load();

        /// <summary>
        /// Validates then atomically writes <paramref name="document"/> to the configured path.
        /// Structural failure refuses the write (no partial file). Structural pass always writes,
        /// even when drift/ability-kit warnings are present (warn-but-allow).
        /// </summary>
        Task<BalanceStandardsSaveResult> SaveAsync(BalanceStandardsDocument document, CancellationToken ct = default);
    }
}
