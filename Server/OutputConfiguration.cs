namespace Hedron.Server
{
    /// <summary>Bound from the <c>Output</c> configuration section.</summary>
    public sealed class OutputConfiguration
    {
        /// <summary>Initial <c>SupportsColor</c> value for new telnet sessions. Default <c>true</c>.</summary>
        public bool DefaultColor { get; set; } = true;
    }
}
