using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hedron.Core.Systems.Definitions;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>The outcome of one help-topic build: the new snapshot and what was found.</summary>
    public sealed record HelpTopicBuildResult(HelpTopicSet Set, IReadOnlyList<ContentDiagnostic> Diagnostics);

    /// <summary>
    /// The pure help-topic decision: validates each file (R9), merges the layers by id with the later
    /// layer winning (a skipped world file never displaces the engine topic), and checks collisions
    /// and <c>seeAlso</c> (R10). Deterministic and I/O-free, so boot, <c>reload</c> and the editor all
    /// apply one rule set. Nothing here ever refuses: invalid files are skipped with an
    /// <see cref="ContentDiagnosticSeverity.Error"/>, problems are <see cref="ContentDiagnosticSeverity.Warning"/>s.
    /// </summary>
    public static class HelpTopicSetBuilder
    {
        private static readonly Regex TokenPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        /// <summary>Trims and lowercases a topic token (id or alias).</summary>
        public static string NormalizeToken(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>Whether <paramref name="token"/> (already normalized) is a valid id or alias.</summary>
        public static bool IsValidToken(string token) => TokenPattern.IsMatch(token);

        public static HelpTopicBuildResult Build(
            IReadOnlyList<HelpLayerRead> layers,
            IReadOnlySet<string> verbTokens,
            IReadOnlySet<string> adminOnlyVerbTokens)
        {
            var diagnostics = new List<ContentDiagnostic>();
            var merged = new Dictionary<string, Draft>(StringComparer.Ordinal);
            var engineIds = new HashSet<string>(StringComparer.Ordinal);
            string? engineRoot = null;

            foreach (var layer in layers)
            {
                var layerName = LayerName(layer.Layer);

                if (layer.Layer == HelpLayer.Engine)
                {
                    engineRoot = layer.Root;
                    if (!layer.Exists)
                    {
                        diagnostics.Add(Warning(layerName, $"engine help directory '{layer.Root}' not found; no engine topics loaded."));
                        continue;
                    }
                }
                else if (!layer.Exists)
                {
                    continue;
                }

                if (layer.Layer == HelpLayer.World && engineRoot is not null && SamePath(layer.Root, engineRoot))
                {
                    diagnostics.Add(Warning(layerName, $"world help directory '{layer.Root}' is the engine help directory; world layer skipped."));
                    continue;
                }

                var inLayer = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in layer.Files)
                {
                    var source = $"{layerName}/{file.FileName}";
                    var draft = ParseFile(file, source, diagnostics);
                    if (draft is null)
                    {
                        var stem = NormalizeToken(Path.GetFileNameWithoutExtension(file.FileName));
                        if (layer.Layer == HelpLayer.World && engineIds.Contains(stem))
                            diagnostics.Add(Warning(source, $"skipped; the engine topic '{stem}' is served instead."));
                        continue;
                    }

                    if (!inLayer.Add(draft.Id))
                    {
                        diagnostics.Add(Warning(source, $"duplicate topic id '{draft.Id}' in the {layerName} layer; skipped (the ordinal-first file wins)."));
                        continue;
                    }

                    if (layer.Layer == HelpLayer.Engine)
                    {
                        engineIds.Add(draft.Id);
                        draft.Origin = HelpTopicOrigin.Engine;
                    }
                    else
                    {
                        draft.Origin = merged.ContainsKey(draft.Id) ? HelpTopicOrigin.WorldOverride : HelpTopicOrigin.World;
                    }

                    merged[draft.Id] = draft;
                }
            }

            ResolveAliases(merged, diagnostics);
            CheckVerbCollisions(merged, verbTokens, adminOnlyVerbTokens, diagnostics);

            var aliasIndex = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var draft in merged.Values)
                foreach (var alias in draft.Aliases)
                    aliasIndex[alias] = draft.Id;

            var topics = merged.Values
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => new HelpTopic(
                    d.Id, d.Title, d.Aliases, d.Body,
                    ResolveSeeAlso(d, merged, aliasIndex, diagnostics),
                    d.Origin, d.SourceFile))
                .ToList();

            return new HelpTopicBuildResult(new HelpTopicSet(topics), diagnostics);
        }

        private static Draft? ParseFile(HelpTopicFileRead file, string source, List<ContentDiagnostic> diagnostics)
        {
            if (file.Error is not null)
            {
                diagnostics.Add(Error(source, $"could not be parsed and was skipped: {file.Error}"));
                return null;
            }

            var doc = file.Document;
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(doc?.Id)) missing.Add("id");
            if (string.IsNullOrWhiteSpace(doc?.Title)) missing.Add("title");
            if (string.IsNullOrWhiteSpace(doc?.Body)) missing.Add("body");
            if (missing.Count > 0)
            {
                diagnostics.Add(Error(source, $"missing {string.Join(", ", missing)}; skipped."));
                return null;
            }

            var id = NormalizeToken(doc!.Id);
            if (!IsValidToken(id))
            {
                diagnostics.Add(Error(source, $"invalid topic id '{doc.Id}' (lowercase letters, digits and single hyphens only); skipped."));
                return null;
            }

            var aliases = new List<string>();
            foreach (var raw in doc.Aliases ?? new List<string>())
            {
                var alias = NormalizeToken(raw);
                if (!IsValidToken(alias))
                {
                    diagnostics.Add(Warning(source, $"invalid alias '{raw}' dropped."));
                    continue;
                }
                if (alias != id && !aliases.Contains(alias))
                    aliases.Add(alias);
            }

            return new Draft
            {
                Id = id,
                Title = doc.Title!.Trim(),
                Body = doc.Body!.TrimEnd(),
                Aliases = aliases,
                SeeAlsoRaw = doc.SeeAlso ?? new List<string>(),
                SourceFile = source,
            };
        }

        // An alias equal to another topic's id is dropped (the id wins); an alias shared by two topics
        // stays with the ordinal-first topic id.
        private static void ResolveAliases(Dictionary<string, Draft> merged, List<ContentDiagnostic> diagnostics)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var draft in merged.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var kept = new List<string>();
                foreach (var alias in draft.Aliases)
                {
                    if (merged.ContainsKey(alias))
                    {
                        diagnostics.Add(Warning(draft.SourceFile, $"alias '{alias}' is another topic's id; dropped."));
                        continue;
                    }
                    if (owners.TryGetValue(alias, out var owner))
                    {
                        diagnostics.Add(Warning(draft.SourceFile, $"alias '{alias}' is already used by topic '{owner}'; dropped."));
                        continue;
                    }
                    owners[alias] = draft.Id;
                    kept.Add(alias);
                }
                draft.Aliases = kept;
            }
        }

        private static void CheckVerbCollisions(
            Dictionary<string, Draft> merged,
            IReadOnlySet<string> verbTokens,
            IReadOnlySet<string> adminOnlyVerbTokens,
            List<ContentDiagnostic> diagnostics)
        {
            foreach (var draft in merged.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var token in new[] { draft.Id }.Concat(draft.Aliases))
                {
                    if (!verbTokens.Contains(token))
                        continue;
                    var audience = adminOnlyVerbTokens.Contains(token) ? " for administrators" : string.Empty;
                    diagnostics.Add(Warning(draft.SourceFile,
                        $"'{token}' is also a command; 'help {token}' shows the command{audience}, not this topic."));
                }
            }
        }

        private static IReadOnlyList<string> ResolveSeeAlso(
            Draft draft,
            Dictionary<string, Draft> merged,
            Dictionary<string, string> aliasIndex,
            List<ContentDiagnostic> diagnostics)
        {
            var resolved = new List<string>();
            foreach (var raw in draft.SeeAlsoRaw)
            {
                var token = NormalizeToken(raw);
                var target = merged.ContainsKey(token) ? token
                    : aliasIndex.TryGetValue(token, out var viaAlias) ? viaAlias
                    : null;
                if (target is null)
                {
                    diagnostics.Add(Warning(draft.SourceFile, $"seeAlso '{raw}' names no topic; left out."));
                    continue;
                }
                if (target != draft.Id && !resolved.Contains(target))
                    resolved.Add(target);
            }
            return resolved;
        }

        private static bool SamePath(string a, string b) => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static string LayerName(HelpLayer layer) => layer == HelpLayer.Engine ? "engine" : "world";

        private static ContentDiagnostic Warning(string source, string message) =>
            new(ContentDiagnosticSeverity.Warning, source, message);

        private static ContentDiagnostic Error(string source, string message) =>
            new(ContentDiagnosticSeverity.Error, source, message);

        private sealed class Draft
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
            public List<string> Aliases { get; set; } = new();
            public List<string> SeeAlsoRaw { get; set; } = new();
            public string SourceFile { get; set; } = string.Empty;
            public HelpTopicOrigin Origin { get; set; }
        }
    }
}
