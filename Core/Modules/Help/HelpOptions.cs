namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// <c>Help:</c> configuration. The engine help layer is a committed, engine-owned install
    /// artifact — it is resolved against the Hedron root (the checkout, or the install folder), never
    /// under <c>Paths:DataDirectory</c>. The world layer is always <c>&lt;World:ContentDirectory&gt;/help/</c>
    /// and has no key of its own.
    /// </summary>
    public sealed class HelpOptions
    {
        public const string DefaultEngineTopicDirectory = "content/help";

        /// <summary>The engine help-topic directory (default <c>content/help</c>).</summary>
        public string EngineTopicDirectory { get; set; } = DefaultEngineTopicDirectory;

        /// <summary>
        /// The world help-topic directory. Not a configuration key: the composition root always
        /// derives it as <c>&lt;World:ContentDirectory&gt;/help</c>, so Help never reads World's options.
        /// <c>DataPaths</c> is its only writer; empty until composed.
        /// </summary>
        public string WorldTopicDirectory { get; set; } = string.Empty;
    }
}
