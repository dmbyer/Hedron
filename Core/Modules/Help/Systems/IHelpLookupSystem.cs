using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// The <c>help</c> lookup decision (R5): exact before prefix, verb → topic → ability within each
    /// tier, with the visibility gate at every tier. A multi-word query walks the topic and ability
    /// tiers with the whole query first, then falls back to its first token as a verb.
    /// Reads command <b>metadata</b> through <c>Lazy&lt;IVerbRegistry&gt;</c> only (never executes a
    /// command) — the sanctioned domain read in <c>01-layers.md</c>. Pure read; publishes nothing.
    /// </summary>
    public interface IHelpLookupSystem
    {
        /// <summary>The verbs visible to <paramref name="invoker"/> plus every topic.</summary>
        HelpIndex BuildIndex(ISession invoker);

        /// <summary>Resolves one query against one topic snapshot (read once per call).</summary>
        HelpResolution Resolve(HelpQuery query, ISession invoker);
    }
}
