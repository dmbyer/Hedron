using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Systems.Definitions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Help topics' <see cref="IContentReloadParticipant"/>: on every successful boot load and
    /// <c>reload</c>, reads both layers, gathers the verb tokens, runs the pure
    /// <see cref="HelpTopicSetBuilder"/>, publishes the new snapshot and returns the diagnostics.
    /// The participant orchestrates; the registry stays a dumb holder. Runs on the loader caller's
    /// thread (confined, INV-31).
    /// </summary>
    /// <remarks>
    /// Takes <c>Lazy&lt;IVerbRegistry&gt;</c>: <c>ReloadCommand</c> → loader → participants →
    /// <c>IVerbRegistry</c> (the dispatcher) → every <c>ICommand</c> would otherwise be a DI cycle.
    /// </remarks>
    public sealed class HelpTopicReloadParticipant : IContentReloadParticipant
    {
        private readonly IHelpTopicLayerReader _reader;
        private readonly HelpTopicRegistry _registry;
        private readonly Lazy<IVerbRegistry> _verbRegistry;

        public HelpTopicReloadParticipant(
            IHelpTopicLayerReader reader, HelpTopicRegistry registry, Lazy<IVerbRegistry> verbRegistry)
        {
            _reader = reader;
            _registry = registry;
            _verbRegistry = verbRegistry;
        }

        public string Name => "help topics";

        public Task<IReadOnlyList<ContentDiagnostic>> RebuildAsync(CancellationToken ct = default)
        {
            var layers = _reader.ReadLayers();
            var verbs = HelpVerbTokens.From(_verbRegistry.Value);
            var result = HelpTopicSetBuilder.Build(layers, verbs.All, verbs.AdminOnly);
            _registry.Publish(result.Set);
            return Task.FromResult(result.Diagnostics);
        }
    }
}
