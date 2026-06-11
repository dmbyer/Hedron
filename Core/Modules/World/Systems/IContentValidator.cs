using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// On-demand content validator. Factored out of the boot-time
    /// <c>RegistryValidationBootstrap</c> so the same referential-integrity rules run in two
    /// call modes: a whole-registry sweep (boot fail-fast + the future bulk generator) and a
    /// single in-memory definition check (the offline authoring editor, per edit).
    /// </summary>
    /// <remarks>
    /// Domain-tier system: it reads the ability/aspect/effect registries (domain → domain is
    /// permitted, INV-1) and returns a structured <see cref="ValidationReport"/> — it never
    /// throws and never publishes (INV-5). The host (the bootstrap) decides fail-fast policy.
    /// </remarks>
    public interface IContentValidator
    {
        /// <summary>
        /// Registry / live-scan mode. Validates ability→effect and ability→aspect cross-refs,
        /// aspect-composition normalization, the supplied starting-ability ids against the
        /// ability registry, and live area-entity aspect-affinity compositions. This is the
        /// boot bootstrap's sweep.
        /// </summary>
        /// <param name="startingAbilityIds">
        /// Ability ids configured as character defaults; each must resolve in the ability
        /// registry. Supplied by the host (which owns the config read), keeping this system
        /// free of configuration coupling.
        /// </param>
        ValidationReport ValidateRegistry(IReadOnlyCollection<string> startingAbilityIds);

        /// <summary>
        /// Single-definition mode. Validates one in-memory authored definition with no live
        /// entities — the per-edit editor path and the pre-write generation path. Returns an
        /// empty report for kinds that carry no single-definition rules yet.
        /// </summary>
        ValidationReport Validate(IEntityTemplate template);
    }
}
