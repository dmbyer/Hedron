namespace Hedron.Core.Output
{
    /// <summary>A plain-text message with a severity hint for future formatting.</summary>
    public sealed record PlainMessage(string Text, OutputSeverity Severity, OutputCategory Category) : IOutputMessage;
}
