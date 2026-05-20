using Hedron.Core.Commands;

namespace Hedron.Core.Output
{
    /// <summary>One row in a help index listing.</summary>
    public sealed record HelpIndexEntry(string Verb, string ShortDescription, CommandCategory Category);
}
