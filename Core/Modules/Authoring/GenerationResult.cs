using System.Collections.Generic;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Outcome of one <see cref="Systems.IContentGenerationSystem.GenerateAsync"/> call: the counts
    /// written per content kind, the full ordered list of derived blueprint ids, and any validation
    /// errors accumulated while validating the emitted definitions. The system returns this; it
    /// never publishes (INV-5). A non-empty <see cref="ValidationErrors"/> is the run-mode's signal
    /// to exit non-zero.
    /// </summary>
    /// <remarks>Pure-data record, no logic.</remarks>
    public sealed record GenerationResult
    {
        public int AreasWritten { get; init; }
        public int RoomsWritten { get; init; }
        public int MobsWritten { get; init; }
        public int ItemsWritten { get; init; }

        /// <summary>Every blueprint id minted this run, in generation order (areas, rooms, mobs, items).</summary>
        public IReadOnlyList<string> BlueprintIds { get; init; } = new List<string>();

        /// <summary>Validation errors against the emitted definitions; empty ⇒ the run is clean.</summary>
        public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
    }
}
