using Hedron.Core.ECS;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Holds session-only status effects that are discarded on server restart.
    /// Not tagged <c>[Persistent]</c> — intentionally excluded from disk serialization.
    /// Content is populated by feature slices (combat stuns, temporary buffs, etc.)
    /// as they are implemented.
    /// </summary>
    public class TransientEffectsComponent : IComponent
    {
        // Populated by future feature slices (Phase 3+).
        // Do not add game-specific fields here without a matching use-case doc entry.
    }
}
