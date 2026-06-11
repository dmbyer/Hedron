using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Composes the existing per-kind content writers + <c>*Template</c> types to emit a swath of
    /// world-content YAML from a <see cref="GenerationProfile"/> — the bulk-generation track of the
    /// content-tooling platform. Driven entirely by an <see cref="Hedron.Core.Systems.IRandom"/>
    /// seeded from the profile, so a fixed-seed run is deterministic and reproducible (INV-26).
    /// </summary>
    /// <remarks>
    /// Domain-tier system. It writes YAML definitions only — it creates <b>no</b> live entities and
    /// registers nothing in the live <c>TemplateRegistry</c> (INV-12/INV-23). It <b>returns a
    /// <see cref="GenerationResult"/>; it never publishes</b> (INV-5) — the run-mode Initiator owns
    /// process-level concerns (validation policy, exit code).
    /// </remarks>
    public interface IContentGenerationSystem
    {
        /// <summary>
        /// Generates and writes content for <paramref name="profile"/>, then returns the counts and
        /// the ordered list of derived blueprint ids. Does not validate — validation is the
        /// caller's (run-mode's) concern via <c>IContentValidator</c>.
        /// </summary>
        Task<GenerationResult> GenerateAsync(GenerationProfile profile, CancellationToken ct = default);
    }
}
