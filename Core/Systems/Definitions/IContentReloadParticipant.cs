using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// A re-derivable definition family that rebuilds whenever world content loads. The world content
    /// loader invokes every DI-registered participant, in registration order, after a
    /// <b>successful</b> boot load or <c>reload</c> (after the starting room resolves), in every host
    /// that loads world content. A refused reload runs none.
    /// </summary>
    /// <remarks>
    /// Core-owned so the loader (World) and a participant (e.g. Help) never reference each other
    /// (INV-2). Participants are <b>warn-only</b>: they report, they never refuse a load — the
    /// opposite of the staged fail-fast posture world content uses. A participant that throws is
    /// reported as one <see cref="ContentDiagnosticSeverity.Error"/> and the load still succeeds.
    /// </remarks>
    public interface IContentReloadParticipant
    {
        /// <summary>Short family name used as the diagnostic source when the participant throws.</summary>
        string Name { get; }

        /// <summary>Rebuilds the family's snapshot from disk and returns what it found.</summary>
        Task<IReadOnlyList<ContentDiagnostic>> RebuildAsync(CancellationToken ct = default);
    }
}
