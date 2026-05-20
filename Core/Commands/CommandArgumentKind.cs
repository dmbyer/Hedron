namespace Hedron.Core.Commands
{
    /// <summary>How a single argument consumes tokens from the raw input tail.</summary>
    public enum CommandArgumentKind
    {
        /// <summary>Consumes exactly one whitespace-delimited token (or double-quoted group).</summary>
        Token,
        /// <summary>Consumes everything from the current position to end-of-line.</summary>
        RestOfLine,
        /// <summary>Reads a leading count token followed by a second token (e.g. "3 coins").</summary>
        Quantified,
    }
}
