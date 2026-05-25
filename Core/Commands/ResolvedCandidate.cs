namespace Hedron.Core.Commands
{
    /// <summary>
    /// A single match candidate returned by <see cref="IArgumentResolver.GetCandidates"/>.
    /// </summary>
    /// <param name="MatchString">The string the player's token is prefix-matched against (item name or keyword alias).</param>
    /// <param name="CanonicalValue">
    /// The value substituted into the parsed argument when this candidate wins.
    /// Multiple <see cref="MatchString"/> values sharing the same <see cref="CanonicalValue"/> collapse
    /// to one match after deduplication — enabling keyword aliases to resolve to a canonical item name
    /// without treating them as ambiguous alternatives.
    /// </param>
    public readonly record struct ResolvedCandidate(string MatchString, string CanonicalValue);
}
