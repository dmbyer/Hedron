using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>Category-grouped command listing (the 'help' and 'commands' output).</summary>
    public sealed record HelpIndexMessage(IReadOnlyList<HelpIndexEntry> Entries) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;
    }
}
