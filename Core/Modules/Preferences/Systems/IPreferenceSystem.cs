using System.Collections.Generic;

namespace Hedron.Core.Modules.Preferences.Systems
{
    /// <summary>One preference's current state for a given entity.</summary>
    public readonly record struct PreferenceState(PreferenceDefinition Definition, bool Enabled);

    /// <summary>
    /// Reads and writes a character's configurable settings, resolving unset preferences to their
    /// <see cref="PreferenceRegistry"/> defaults. Returns values and publishes nothing (INV-5) —
    /// the <c>config</c> command publishes <c>PreferenceChangedEvent</c>.
    /// </summary>
    public interface IPreferenceSystem
    {
        /// <summary>
        /// Whether <paramref name="preference"/> is on for <paramref name="entityId"/>. An entity
        /// with no <see cref="Components.PlayerConfigurationComponent"/>, or one that has never set
        /// this preference, reads the shipped default.
        /// </summary>
        bool IsEnabled(uint entityId, PreferenceId preference);

        /// <summary>
        /// Sets <paramref name="preference"/>, attaching
        /// <see cref="Components.PlayerConfigurationComponent"/> on first write.
        /// </summary>
        void Set(uint entityId, PreferenceId preference, bool enabled);

        /// <summary>Every registered preference with its current effective state, in display order.</summary>
        IReadOnlyList<PreferenceState> GetAll(uint entityId);
    }
}
