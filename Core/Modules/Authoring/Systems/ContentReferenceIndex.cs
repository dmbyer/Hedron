using System;
using System.Collections.Generic;
using System.IO;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Default <see cref="IContentReferenceIndex"/>. Builds its answer from the on-disk YAML
    /// definition set via the existing <see cref="IContentSerializer"/>; the declared edge set
    /// is registered as data so every consumer (resolve, referrer lookup, broken sweep,
    /// per-definition check) shares one code path (INV-19).
    /// </summary>
    /// <remarks>
    /// Pure read — no event bus, no entity service, no persistence (INV-5). File IO is
    /// performed on every call so callers always see the current on-disk state; caching
    /// is a future optimisation (not needed for the offline editor throughput).
    /// </remarks>
    public sealed class ContentReferenceIndex : IContentReferenceIndex
    {
        // ── Declared edge tuple ──────────────────────────────────────────────────────

        /// <summary>
        /// An edge declaration: source kind + extractor that yields (fieldLabel, targetId) pairs
        /// from a template, plus the target kind the ids refer to.
        /// </summary>
        private sealed class EdgeDeclaration
        {
            public ContentKind SourceKind { get; }
            public ContentKind TargetKind { get; }

            /// <summary>
            /// Extracts zero or more (fieldLabel, targetId) references from a source template.
            /// Returns an empty enumerable when the field is blank or absent.
            /// </summary>
            public Func<IEntityTemplate, IEnumerable<(string FieldLabel, string TargetId)>> Extract { get; }

            public EdgeDeclaration(
                ContentKind sourceKind,
                ContentKind targetKind,
                Func<IEntityTemplate, IEnumerable<(string FieldLabel, string TargetId)>> extract)
            {
                SourceKind = sourceKind;
                TargetKind = targetKind;
                Extract = extract;
            }
        }

        // ── Declared edge set ────────────────────────────────────────────────────────

        /// <summary>
        /// The five declared cross-definition reference edges. Adding a new edge is a one-line
        /// data change here — all consumers pick it up automatically (INV-19).
        /// </summary>
        private static readonly EdgeDeclaration[] DeclaredEdges = new[]
        {
            // (Room, AreaId) → Area
            new EdgeDeclaration(
                ContentKind.Room,
                ContentKind.Area,
                t => t is RoomTemplate r && !string.IsNullOrEmpty(r.AreaId)
                    ? new[] { ("AreaId", r.AreaId) }
                    : Array.Empty<(string, string)>()),

            // (Room, Exits[dir]) → Room  — one tuple per non-blank exit entry
            new EdgeDeclaration(
                ContentKind.Room,
                ContentKind.Room,
                t =>
                {
                    if (t is not RoomTemplate r)
                        return Array.Empty<(string, string)>();

                    var refs = new List<(string, string)>();
                    foreach (var kv in r.Exits)
                    {
                        if (!string.IsNullOrEmpty(kv.Value))
                            refs.Add(($"Exits[{kv.Key}]", kv.Value));
                    }
                    return refs;
                }),

            // (Item, SpawnRoomBlueprintId) → Room
            new EdgeDeclaration(
                ContentKind.Item,
                ContentKind.Room,
                t => t is ItemTemplate i && !string.IsNullOrEmpty(i.SpawnRoomBlueprintId)
                    ? new[] { ("SpawnRoomBlueprintId", i.SpawnRoomBlueprintId) }
                    : Array.Empty<(string, string)>()),

            // (Mob, SpawnRoomBlueprintId) → Room
            new EdgeDeclaration(
                ContentKind.Mob,
                ContentKind.Room,
                t => t is MobTemplate m && !string.IsNullOrEmpty(m.SpawnRoomBlueprintId)
                    ? new[] { ("SpawnRoomBlueprintId", m.SpawnRoomBlueprintId) }
                    : Array.Empty<(string, string)>()),

            // (Area, Rooms[]) → Room  — one tuple per room blueprint id in the area's Rooms list
            new EdgeDeclaration(
                ContentKind.Area,
                ContentKind.Room,
                t =>
                {
                    if (t is not AreaTemplate a)
                        return Array.Empty<(string, string)>();

                    var refs = new List<(string, string)>();
                    foreach (var roomId in a.Rooms)
                    {
                        if (!string.IsNullOrEmpty(roomId))
                            refs.Add(("Rooms[]", roomId));
                    }
                    return refs;
                }),
        };

        // ── Dependencies ─────────────────────────────────────────────────────────────

        private readonly IContentSerializer _serializer;
        private readonly ILogger<ContentReferenceIndex> _logger;
        private readonly string _contentDirectory;

        public ContentReferenceIndex(
            IContentSerializer serializer,
            IOptions<WorldOptions> options,
            ILogger<ContentReferenceIndex> logger)
        {
            _serializer = serializer;
            _logger = logger;
            _contentDirectory = options.Value.ContentDirectory;
        }

        // ── IContentReferenceIndex ───────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool Resolves(ContentKind targetKind, string targetBlueprintId)
        {
            if (string.IsNullOrEmpty(targetBlueprintId))
                return false;

            var path = DefinitionPath(targetKind, targetBlueprintId);
            return File.Exists(path);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ReferrerEdit> Referrers(ContentKind targetKind, string targetBlueprintId)
        {
            var result = new List<ReferrerEdit>();

            foreach (var edge in DeclaredEdges)
            {
                if (edge.TargetKind != targetKind)
                    continue;

                foreach (var template in LoadAllOfKind(edge.SourceKind))
                {
                    foreach (var (fieldLabel, targetId) in edge.Extract(template))
                    {
                        if (string.Equals(targetId, targetBlueprintId, StringComparison.Ordinal))
                            result.Add(new ReferrerEdit(edge.SourceKind, template.BlueprintId, fieldLabel));
                    }
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public IReadOnlyList<BrokenReference> SweepBroken()
        {
            var result = new List<BrokenReference>();

            foreach (var edge in DeclaredEdges)
            {
                foreach (var template in LoadAllOfKind(edge.SourceKind))
                {
                    foreach (var (fieldLabel, targetId) in edge.Extract(template))
                    {
                        if (!Resolves(edge.TargetKind, targetId))
                        {
                            result.Add(new BrokenReference(
                                edge.SourceKind,
                                template.BlueprintId,
                                fieldLabel,
                                targetId));
                        }
                    }
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public IReadOnlyList<BrokenReference> BrokenFor(IEntityTemplate definition)
        {
            var result = new List<BrokenReference>();

            // Infer the kind from the concrete template type so callers don't have to pass it.
            var kind = KindOf(definition);
            if (kind is null)
                return result;

            foreach (var edge in DeclaredEdges)
            {
                if (edge.SourceKind != kind.Value)
                    continue;

                foreach (var (fieldLabel, targetId) in edge.Extract(definition))
                {
                    if (!Resolves(edge.TargetKind, targetId))
                    {
                        result.Add(new BrokenReference(
                            edge.SourceKind,
                            definition.BlueprintId,
                            fieldLabel,
                            targetId));
                    }
                }
            }

            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Loads and deserializes all definition files of the given kind from disk. Skips
        /// unparseable files with a warning — never throws (mirrors the catalog's read pattern).
        /// </summary>
        private IEnumerable<IEntityTemplate> LoadAllOfKind(ContentKind kind)
        {
            var directory = Path.Combine(_contentDirectory, kind.Subdirectory());
            if (!Directory.Exists(directory))
                yield break;

            foreach (var file in Directory.GetFiles(directory, "*" + _serializer.FormatExtension))
            {
                IEntityTemplate? template = null;
                try
                {
                    var body = File.ReadAllText(file);
                    template = _serializer.Deserialize(kind.KindString(), body);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ContentReferenceIndex: failed to read {Kind} file '{File}'; skipping.",
                        kind, file);
                }

                if (template is not null)
                    yield return template;
            }
        }

        /// <summary>
        /// Returns the on-disk path for a definition by kind and blueprint id.
        /// </summary>
        private string DefinitionPath(ContentKind kind, string blueprintId) =>
            Path.Combine(_contentDirectory, kind.Subdirectory(), blueprintId + _serializer.FormatExtension);

        /// <summary>
        /// Infers the <see cref="ContentKind"/> from a concrete template type.
        /// Returns <c>null</c> for unrecognised template types (defensively — new template kinds
        /// must be added here when new content kinds are introduced).
        /// </summary>
        private static ContentKind? KindOf(IEntityTemplate template) => template switch
        {
            AreaTemplate => ContentKind.Area,
            RoomTemplate => ContentKind.Room,
            ItemTemplate => ContentKind.Item,
            MobTemplate  => ContentKind.Mob,
            _            => null,
        };
    }
}
