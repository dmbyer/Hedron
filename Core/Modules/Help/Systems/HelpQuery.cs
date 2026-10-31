using System;
using System.Linq;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// A parsed <c>help</c> argument (R2). <see cref="Raw"/> is the typed text with one pair of
    /// surrounding quotes removed (matching the old token parser's unquoting); <see cref="TopicKey"/>
    /// is its whitespace runs joined by hyphens and lowercased, so <c>help getting started</c>,
    /// <c>help Getting-Started</c> and <c>help getting-started</c> all key to <c>getting-started</c>.
    /// </summary>
    public readonly record struct HelpQuery(string Raw, string TopicKey, string FirstToken, bool IsSingleToken)
    {
        /// <summary>Nothing to look up — bare <c>help</c>, or only whitespace/hyphens: show the index.</summary>
        public bool IsEmpty => TopicKey.Trim('-').Length == 0;

        public static HelpQuery Parse(string? input)
        {
            var text = (input ?? string.Empty).Trim();
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                text = text[1..^1].Trim();

            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var key = string.Join("-", tokens).ToLowerInvariant();
            return new HelpQuery(text, key, tokens.FirstOrDefault() ?? string.Empty, tokens.Length == 1);
        }
    }
}
