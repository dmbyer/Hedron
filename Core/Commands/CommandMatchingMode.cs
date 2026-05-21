namespace Hedron.Core.Commands
{
    /// <summary>
    /// Controls whether the dispatcher resolves this command by prefix or requires a full match.
    /// Player commands default to <see cref="Partial"/>; admin commands default to <see cref="Full"/>.
    /// </summary>
    public enum CommandMatchingMode
    {
        /// <summary>
        /// Prefix resolution enabled. The shortest unambiguous prefix dispatches this command.
        /// E.g. a player typing "lo" resolves to "look".
        /// </summary>
        Partial,

        /// <summary>
        /// Exact match required. The full command name (or a declared alias) must be typed.
        /// Use for high-impact admin commands where an accidental prefix match is dangerous.
        /// </summary>
        Full,
    }
}
