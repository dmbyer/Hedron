namespace Hedron.Core.Output
{
    /// <summary>Detailed help for a single verb (the 'help &lt;verb&gt;' output).</summary>
    public sealed record HelpEntryMessage(string Verb, string LongDescription, string Usage) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Help;
    }
}
