using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Output;
using Hedron.Core.Systems.Definitions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Default <see cref="IHelpTopicAuthoring"/>. Concurrency (INV-31): reads are lock-free over
    /// disk; every write runs under one <see cref="SemaphoreSlim"/>(1,1) gate, so two circuits saving
    /// at once serialize (last save wins). Takes <c>Lazy&lt;IVerbRegistry&gt;</c> for the R10 verb
    /// collision warnings — command metadata only.
    /// </summary>
    public sealed class HelpTopicAuthoring : IHelpTopicAuthoring
    {
        private readonly IHelpTopicLayerReader _reader;
        private readonly Lazy<IVerbRegistry> _verbRegistry;
        private readonly YamlDefinitionDirectory<HelpTopicDocument> _world;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public HelpTopicAuthoring(IHelpTopicLayerReader reader, Lazy<IVerbRegistry> verbRegistry)
        {
            _reader = reader;
            _verbRegistry = verbRegistry;
            _world = new YamlDefinitionDirectory<HelpTopicDocument>(reader.WorldRoot, new YamlCodecOptions(OmitNulls: true));
        }

        public HelpTopicCatalogView List()
        {
            var result = Build();
            return new HelpTopicCatalogView(result.Set.Ordered, result.Diagnostics);
        }

        public HelpTopicEditView? Load(string id)
        {
            var key = HelpTopicSetBuilder.NormalizeToken(id);
            var set = Build().Set;
            if (!set.TryGet(key, out var topic))
                return null;
            return new HelpTopicEditView(topic, topic.Origin != HelpTopicOrigin.Engine, topic.Origin == HelpTopicOrigin.WorldOverride);
        }

        // Builds a transient topic and renders it through the same HelpTopicRendering mapping `help` uses.
        public HelpTopicMessage Preview(HelpTopicDraft draft)
        {
            var current = Build().Set;
            var id = HelpTopicSetBuilder.NormalizeToken(draft.Id);
            var seeAlso = draft.SeeAlso
                .Select(s => current.TryResolve(s, out var target) ? target.Id : null)
                .OfType<string>()
                .Where(s => s != id)
                .Distinct()
                .ToList();
            var topic = new HelpTopic(id, draft.Title.Trim(), Array.Empty<string>(), draft.Body.TrimEnd(),
                seeAlso, HelpTopicOrigin.World, SourceFile: string.Empty);
            return HelpTopicRendering.ToMessage(topic);
        }

        public IReadOnlyList<string> Validate(HelpTopicDraft draft)
        {
            var errors = new List<string>();
            if (_reader.WorldRoot.Length == 0)
                errors.Add("no world help directory is configured.");
            var id = HelpTopicSetBuilder.NormalizeToken(draft.Id);
            if (id.Length == 0)
                errors.Add("id is required.");
            else if (!HelpTopicSetBuilder.IsValidToken(id))
                errors.Add($"id '{draft.Id}' is invalid: use lowercase letters, digits and single hyphens.");
            if (string.IsNullOrWhiteSpace(draft.Title))
                errors.Add("title is required.");
            if (string.IsNullOrWhiteSpace(draft.Body))
                errors.Add("body is required.");
            return errors;
        }

        public async Task<ContentWriteResult> SaveAsync(HelpTopicDraft draft, string? previousId = null, CancellationToken ct = default)
        {
            var id = HelpTopicSetBuilder.NormalizeToken(draft.Id);
            var errors = Validate(draft);
            if (errors.Count > 0)
                return ContentWriteResult.Failed(id, errors);

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Files in the world layer that currently hold this topic (or the one being renamed).
                var previous = previousId is null ? id : HelpTopicSetBuilder.NormalizeToken(previousId);
                var stale = WorldFilesFor(id).Concat(previous == id ? Enumerable.Empty<string>() : WorldFilesFor(previous)).ToList();

                var written = await _world.WriteAsync(id, ToDocument(draft, id), ct).ConfigureAwait(false);

                foreach (var path in stale.Where(p => !SamePath(p, written)))
                    _world.Delete(path);

                return Result(id, Build());
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task<ContentWriteResult> OverrideAsync(string engineId, CancellationToken ct = default)
        {
            var id = HelpTopicSetBuilder.NormalizeToken(engineId);
            if (_reader.WorldRoot.Length == 0)
                return ContentWriteResult.Failed(id, new[] { "no world help directory is configured." });

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var current = Build().Set;
                if (!current.TryGet(id, out var topic))
                    return ContentWriteResult.Failed(id, new[] { $"no help topic '{id}'." });
                if (topic.Origin != HelpTopicOrigin.Engine || WorldFilesFor(id).Any())
                    return ContentWriteResult.Failed(id, new[] { $"'{id}' already has a world copy; edit that instead." });

                await _world.WriteAsync(id, ToDocument(
                    new HelpTopicDraft(topic.Id, topic.Title, topic.Aliases, topic.SeeAlso, topic.Body), id), ct).ConfigureAwait(false);
                return Result(id, Build());
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task<ContentWriteResult> DeleteAsync(string id, CancellationToken ct = default)
        {
            var key = HelpTopicSetBuilder.NormalizeToken(id);

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var files = WorldFilesFor(key).ToList();
                if (files.Count == 0)
                    return ContentWriteResult.Failed(key, new[] { $"'{key}' is not a world topic; engine topics cannot be deleted." });

                foreach (var path in files)
                    _world.Delete(path);
                return ContentWriteResult.Ok(key);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private HelpTopicBuildResult Build()
        {
            var verbs = HelpVerbTokens.From(_verbRegistry.Value);
            return HelpTopicSetBuilder.Build(_reader.ReadLayers(), verbs.All, verbs.AdminOnly);
        }

        // World files whose document declares this id, plus <id>.yaml itself (even if unparseable).
        private IEnumerable<string> WorldFilesFor(string id)
        {
            if (id.Length == 0 || !Directory.Exists(_world.Directory))
                return Enumerable.Empty<string>();

            return _world.List(doc => doc.Id)
                .Where(entry =>
                    HelpTopicSetBuilder.NormalizeToken(entry.Name) == id ||
                    string.Equals(Path.GetFileNameWithoutExtension(entry.FileName), id, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Path)
                .ToList();
        }

        private static ContentWriteResult Result(string id, HelpTopicBuildResult build)
        {
            var file = $"world/{id}.yaml";
            var warnings = build.Diagnostics.Where(d => d.Source == file).Select(d => d.Message).ToList();
            return warnings.Count == 0 ? ContentWriteResult.Ok(id) : ContentWriteResult.OkWithWarnings(id, warnings);
        }

        private static HelpTopicDocument ToDocument(HelpTopicDraft draft, string id) => new()
        {
            Id = id,
            Title = draft.Title.Trim(),
            Aliases = Clean(draft.Aliases),
            SeeAlso = Clean(draft.SeeAlso),
            Body = draft.Body.TrimEnd() + "\n",
        };

        private static List<string>? Clean(IReadOnlyList<string> values)
        {
            var cleaned = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            return cleaned.Count == 0 ? null : cleaned;
        }

        private static bool SamePath(string a, string b) => string.Equals(
            Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
