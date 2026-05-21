using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>Detailed help for a single verb (the 'help &lt;verb&gt;' output).</summary>
    public sealed record HelpEntryMessage(
        string Verb,
        string LongDescription,
        string Usage,
        IReadOnlyList<string> Aliases) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;
    }
}
