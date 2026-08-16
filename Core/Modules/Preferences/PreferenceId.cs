namespace Hedron.Core.Modules.Preferences
{
    /// <summary>
    /// The stable key of one player-configurable setting. Serialized by name into
    /// <see cref="Components.PlayerConfigurationComponent"/>, so entries must not be renamed
    /// without a migration — adding is free, an absent key falls back to the registry default.
    /// </summary>
    public enum PreferenceId
    {
        /// <summary>Show a line each time a track gains experience.</summary>
        ProgressionXpMessages,

        /// <summary>Show a line each time a track crosses a threshold (improves / gains a rank).</summary>
        ProgressionImprovementMessages,
    }
}
