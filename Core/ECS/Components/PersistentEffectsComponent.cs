using Hedron.Core.ECS;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Holds persistent status effects that survive server restart.
    /// Tagged <c>[Persistent]</c> so that <c>PersistenceSystem</c> includes it on save.
    /// Content is populated by feature slices (buffs, debuffs, ongoing spell effects, etc.)
    /// as they are implemented.
    /// </summary>
    [Persistent]
    public class PersistentEffectsComponent : IComponent
    {
        // Populated by future feature slices (Phase 3+).
        // Do not add game-specific fields here without a matching use-case doc entry.
    }
}
