using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Preferences.Components
{
    /// <summary>
    /// A character's configurable settings. Sparse by design: only preferences the player has
    /// explicitly set are stored, and an absent key falls back to
    /// <see cref="PreferenceRegistry"/>'s default — so changing a shipped default takes effect for
    /// every player who never touched it, and no migration is needed when a preference is added.
    ///
    /// <para>
    /// <c>[Persistent]</c> and attached to player characters (which already carry
    /// <c>PersistentEntity</c>, INV-23), so a player's configuration survives restart.
    /// </para>
    ///
    /// <para>
    /// This is the component named in <c>reference/components-planned.md</c> as
    /// "prompt template, preferences". This slice implements the preferences half; the
    /// prompt-template field folds in when the prompt slice needs it, without a second component.
    /// </para>
    /// </summary>
    [Persistent]
    public sealed class PlayerConfigurationComponent : IComponent
    {
        /// <summary>Explicitly-set boolean preferences. Absent key = registry default.</summary>
        public Dictionary<PreferenceId, bool> Preferences { get; set; } = new();
    }
}
