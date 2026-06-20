using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Default <see cref="IContentDefinitionCatalog"/>. Composes the existing per-kind content
    /// writers, the content serializer (read side), the content validator, and the reference index
    /// into one surface-agnostic authoring facade.
    /// </summary>
    public sealed class ContentDefinitionCatalog : IContentDefinitionCatalog
    {
        private readonly IContentSerializer _serializer;
        private readonly IContentValidator _validator;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IContentReferenceIndex _referenceIndex;
        private readonly IAreaContentWriter _areaWriter;
        private readonly IRoomContentWriter _roomWriter;
        private readonly IItemContentWriter _itemWriter;
        private readonly IMobContentWriter _mobWriter;
        private readonly ILogger<ContentDefinitionCatalog> _logger;
        private readonly string _contentDirectory;

        public ContentDefinitionCatalog(
            IContentSerializer serializer,
            IContentValidator validator,
            ITemplateRegistry templateRegistry,
            IAreaContentWriter areaWriter,
            IRoomContentWriter roomWriter,
            IItemContentWriter itemWriter,
            IMobContentWriter mobWriter,
            IOptions<WorldOptions> options,
            ILogger<ContentDefinitionCatalog> logger)
            : this(
                serializer,
                validator,
                templateRegistry,
                new ContentReferenceIndex(serializer, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentReferenceIndex>.Instance),
                areaWriter,
                roomWriter,
                itemWriter,
                mobWriter,
                options,
                logger)
        {
        }

        /// <summary>
        /// Primary constructor — accepts an explicit <see cref="IContentReferenceIndex"/>. Used in
        /// tests and production DI equally.
        /// </summary>
        public ContentDefinitionCatalog(
            IContentSerializer serializer,
            IContentValidator validator,
            ITemplateRegistry templateRegistry,
            IContentReferenceIndex referenceIndex,
            IAreaContentWriter areaWriter,
            IRoomContentWriter roomWriter,
            IItemContentWriter itemWriter,
            IMobContentWriter mobWriter,
            IOptions<WorldOptions> options,
            ILogger<ContentDefinitionCatalog> logger)
        {
            _serializer = serializer;
            _validator = validator;
            _templateRegistry = templateRegistry;
            _referenceIndex = referenceIndex;
            _areaWriter = areaWriter;
            _roomWriter = roomWriter;
            _itemWriter = itemWriter;
            _mobWriter = mobWriter;
            _logger = logger;
            _contentDirectory = options.Value.ContentDirectory;
        }

        public IReadOnlyList<ContentSummary> List(ContentKind kind)
        {
            var directory = Path.Combine(_contentDirectory, kind.Subdirectory());
            if (!Directory.Exists(directory))
                return Array.Empty<ContentSummary>();

            // Build a room blueprintId → AreaId map once per List call so that item/mob
            // two-hop resolution is O(1) per definition rather than re-reading per file.
            var roomAreaMap = BuildRoomAreaMap();

            var summaries = new List<ContentSummary>();
            foreach (var file in Directory.GetFiles(directory, "*" + _serializer.FormatExtension))
            {
                try
                {
                    var body = File.ReadAllText(file);
                    var template = _serializer.Deserialize(kind.KindString(), body);
                    var (name, description) = ExtractNameAndDescription(template);
                    var areaBlueprintId = ResolveAreaBlueprintId(template, roomAreaMap);
                    summaries.Add(new ContentSummary(template.BlueprintId, name, description, areaBlueprintId));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ContentDefinitionCatalog: failed to read {Kind} file '{File}'; skipping in listing.",
                        kind, file);
                }
            }
            return summaries;
        }

        public IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId)
        {
            var roomSummaries = List(ContentKind.Room);
            var result = new List<ContentSummary>();
            foreach (var summary in roomSummaries)
            {
                if (summary.AreaBlueprintId == areaBlueprintId)
                    result.Add(summary);
            }
            return result;
        }

        public ContentDefinition? Load(ContentKind kind, string blueprintId)
        {
            var path = Path.Combine(_contentDirectory, kind.Subdirectory(), blueprintId + _serializer.FormatExtension);
            if (!File.Exists(path))
                return null;

            var body = File.ReadAllText(path);
            var template = _serializer.Deserialize(kind.KindString(), body);
            return new ContentDefinition(kind, template);
        }

        public async Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default)
        {
            var report = _validator.Validate(definition.Template);
            if (!report.IsValid)
                return ContentWriteResult.Failed(definition.BlueprintId, report.Errors);

            // Write the file before checking cross-references — warn-but-allow (INV-19).
            await WriteDefinitionAsync(definition, ct).ConfigureAwait(false);

            // Surface any dangling cross-references as non-blocking warnings.
            var broken = _referenceIndex.BrokenFor(definition.Template);
            if (broken.Count > 0)
            {
                var warnings = BuildBrokenRefWarnings(broken);
                return ContentWriteResult.OkWithWarnings(definition.BlueprintId, warnings);
            }

            return ContentWriteResult.Ok(definition.BlueprintId);
        }

        public async Task<ContentWriteResult> SaveRoomAsync(
            RoomTemplate room,
            bool bidirectional,
            CancellationToken ct = default)
        {
            var definition = new ContentDefinition(ContentKind.Room, room);

            var report = _validator.Validate(room);
            if (!report.IsValid)
                return ContentWriteResult.Failed(room.BlueprintId, report.Errors);

            await _roomWriter.WriteAsync(room, ct).ConfigureAwait(false);

            var warnings = new List<string>();

            // Warn-but-allow cross-reference check on the room itself.
            var broken = _referenceIndex.BrokenFor(room);
            if (broken.Count > 0)
                warnings.AddRange(BuildBrokenRefWarnings(broken));

            // Bidirectional exit linking.
            if (bidirectional)
            {
                foreach (var (dir, targetBlueprintId) in room.Exits)
                {
                    if (string.IsNullOrEmpty(targetBlueprintId))
                        continue;

                    // Self-loop — silent no-op.
                    if (string.Equals(targetBlueprintId, room.BlueprintId, StringComparison.Ordinal))
                        continue;

                    var targetDef = Load(ContentKind.Room, targetBlueprintId);
                    if (targetDef is null)
                    {
                        // Target doesn't exist yet (dangling); skip silently — the cross-ref
                        // warning from BrokenFor above already covers it.
                        continue;
                    }

                    var targetRoom = (RoomTemplate)targetDef.Template;
                    var inverseDir = dir.Opposite();

                    if (targetRoom.Exits.TryGetValue(inverseDir, out var existingInverse))
                    {
                        if (string.Equals(existingInverse, room.BlueprintId, StringComparison.Ordinal))
                        {
                            // Already-correct inverse — silent no-op.
                            continue;
                        }

                        // Conflict: target already has a different exit in the inverse direction.
                        warnings.Add(
                            $"Bidirectional link skipped: room '{targetBlueprintId}' already has " +
                            $"{inverseDir} → '{existingInverse}' (would overwrite with '{room.BlueprintId}'). " +
                            $"Update '{targetBlueprintId}' manually if needed.");
                        continue;
                    }

                    // Write the inverse exit on the target room.
                    targetRoom.Exits[inverseDir] = room.BlueprintId;
                    await _roomWriter.WriteAsync(targetRoom, ct).ConfigureAwait(false);
                }
            }

            return warnings.Count > 0
                ? ContentWriteResult.OkWithWarnings(room.BlueprintId, warnings)
                : ContentWriteResult.Ok(room.BlueprintId);
        }

        public async Task<ContentDeleteResult> DeleteAsync(
            ContentKind kind,
            string blueprintId,
            CancellationToken ct = default)
        {
            // 1. Find all referrers before deleting the file so the index can still scan.
            var referrers = _referenceIndex.Referrers(kind, blueprintId);

            // 2. Cascade-clear each referrer via the matching writer.
            var cascadeEdits = new List<ReferrerEdit>();
            foreach (var referrer in referrers)
            {
                var applied = await TryCascadeClearAsync(referrer, blueprintId, ct).ConfigureAwait(false);
                if (applied)
                    cascadeEdits.Add(referrer);
            }

            // 3. Delete the target YAML file. (File-only — no EntityService, no SQLite, INV-22/23.)
            var filePath = DefinitionPath(kind, blueprintId);
            if (File.Exists(filePath))
                File.Delete(filePath);

            return new ContentDeleteResult(filePath, blueprintId, cascadeEdits);
        }

        public ContentDefinition CreateNew(ContentKind kind, string name)
        {
            var blueprintId = AdhocBlueprintId.Generate(
                kind.AdhocPrefix(),
                id => _templateRegistry.TryGet(id, out _));

            IEntityTemplate template = kind switch
            {
                ContentKind.Area => new AreaTemplate(blueprintId) { Name = name },
                ContentKind.Room => new RoomTemplate(blueprintId) { Name = name },
                ContentKind.Item => new ItemTemplate(blueprintId) { Name = name },
                ContentKind.Mob  => new MobTemplate(blueprintId)  { Name = name },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            return new ContentDefinition(kind, template);
        }

        // ── Cascade-clear helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the cascade-clear described by <paramref name="referrer"/> and writes the
        /// updated definition via the matching writer. Returns <c>true</c> if the edit was
        /// applied and written; <c>false</c> on any error (logs and continues — best-effort cascade).
        /// </summary>
        private async Task<bool> TryCascadeClearAsync(
            ReferrerEdit referrer,
            string deletedBlueprintId,
            CancellationToken ct)
        {
            try
            {
                var def = Load(referrer.ReferrerKind, referrer.ReferrerBlueprintId);
                if (def is null)
                {
                    _logger.LogWarning(
                        "ContentDefinitionCatalog.Delete: referrer {Kind} '{Id}' not found on disk; skipping cascade.",
                        referrer.ReferrerKind, referrer.ReferrerBlueprintId);
                    return false;
                }

                switch (referrer.ReferrerKind)
                {
                    case ContentKind.Room:
                    {
                        var room = (RoomTemplate)def.Template;
                        if (referrer.FieldLabel == "AreaId")
                        {
                            room.AreaId = string.Empty;
                        }
                        else if (referrer.FieldLabel.StartsWith("Exits[", StringComparison.Ordinal))
                        {
                            // Parse the direction from "Exits[North]".
                            var dirName = referrer.FieldLabel["Exits[".Length..^1];
                            if (Enum.TryParse<Direction>(dirName, out var dir))
                                room.Exits.Remove(dir);
                        }
                        await _roomWriter.WriteAsync(room, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Item:
                    {
                        var item = (ItemTemplate)def.Template;
                        item.SpawnRoomBlueprintId = string.Empty;
                        await _itemWriter.WriteAsync(item, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Mob:
                    {
                        var mob = (MobTemplate)def.Template;
                        mob.SpawnRoomBlueprintId = string.Empty;
                        await _mobWriter.WriteAsync(mob, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Area:
                    {
                        var area = (AreaTemplate)def.Template;
                        area.Rooms.Remove(deletedBlueprintId);
                        await _areaWriter.WriteAsync(area, ct).ConfigureAwait(false);
                        return true;
                    }

                    default:
                        _logger.LogWarning(
                            "ContentDefinitionCatalog.Delete: unhandled referrer kind {Kind}; skipping.",
                            referrer.ReferrerKind);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ContentDefinitionCatalog.Delete: failed to cascade-clear referrer {Kind} '{Id}'; skipping.",
                    referrer.ReferrerKind, referrer.ReferrerBlueprintId);
                return false;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatches the appropriate writer for a definition (all kinds except Room with
        /// bidirectional). For Room with bidirectional, use <see cref="SaveRoomAsync"/> instead.
        /// </summary>
        private async Task WriteDefinitionAsync(ContentDefinition definition, CancellationToken ct)
        {
            switch (definition.Kind)
            {
                case ContentKind.Area:
                    await _areaWriter.WriteAsync((AreaTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Room:
                    await _roomWriter.WriteAsync((RoomTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Item:
                    await _itemWriter.WriteAsync((ItemTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Mob:
                    await _mobWriter.WriteAsync((MobTemplate)definition.Template, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(definition));
            }
        }

        /// <summary>
        /// Returns the on-disk path for a definition by kind and blueprint id.
        /// </summary>
        private string DefinitionPath(ContentKind kind, string blueprintId) =>
            Path.Combine(_contentDirectory, kind.Subdirectory(), blueprintId + _serializer.FormatExtension);

        /// <summary>
        /// Builds human-readable warning strings from a list of <see cref="BrokenReference"/>
        /// instances for inclusion in <see cref="ContentWriteResult.Warnings"/>.
        /// </summary>
        private static IReadOnlyList<string> BuildBrokenRefWarnings(
            IReadOnlyList<BrokenReference> broken)
        {
            var warnings = new List<string>(broken.Count);
            foreach (var b in broken)
            {
                warnings.Add(
                    $"Cross-reference warning: {b.FieldLabel} → '{b.MissingTargetId}' " +
                    $"does not resolve on disk. The file was written; " +
                    $"fix or delete the target before reloading the world.");
            }
            return warnings;
        }

        /// <summary>
        /// Builds a snapshot map of room blueprint id → area id by reading all room files. Used
        /// once per <see cref="List"/> call to make item/mob two-hop resolution O(1) per entry.
        /// A room with a blank <c>AreaId</c> is omitted from the map (yielding <c>null</c> on
        /// lookup) — never throws.
        /// </summary>
        private Dictionary<string, string> BuildRoomAreaMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var roomDirectory = Path.Combine(_contentDirectory, ContentKind.Room.Subdirectory());
            if (!Directory.Exists(roomDirectory))
                return map;

            foreach (var file in Directory.GetFiles(roomDirectory, "*" + _serializer.FormatExtension))
            {
                try
                {
                    var body = File.ReadAllText(file);
                    if (_serializer.Deserialize(ContentKind.Room.KindString(), body) is RoomTemplate room
                        && !string.IsNullOrEmpty(room.AreaId))
                    {
                        map[room.BlueprintId] = room.AreaId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ContentDefinitionCatalog: failed to read room file '{File}' while building area map; skipping.",
                        file);
                }
            }
            return map;
        }

        /// <summary>
        /// Resolves the area blueprint id for a template using the pre-built room→area map.
        /// <list type="bullet">
        ///   <item><see cref="RoomTemplate"/> — one hop: its own <c>AreaId</c> (null if blank).</item>
        ///   <item><see cref="ItemTemplate"/> / <see cref="MobTemplate"/> — two hops:
        ///     <c>SpawnRoomBlueprintId</c> → map lookup → <c>AreaId</c>.</item>
        ///   <item>All other templates (e.g. <see cref="AreaTemplate"/>) — returns <c>null</c>.</item>
        /// </list>
        /// Blank, missing, or dangling references yield <c>null</c> and never throw.
        /// </summary>
        private static string? ResolveAreaBlueprintId(
            IEntityTemplate template,
            Dictionary<string, string> roomAreaMap) => template switch
        {
            RoomTemplate r =>
                string.IsNullOrEmpty(r.AreaId) ? null : r.AreaId,

            ItemTemplate i =>
                !string.IsNullOrEmpty(i.SpawnRoomBlueprintId)
                    && roomAreaMap.TryGetValue(i.SpawnRoomBlueprintId, out var iArea)
                ? iArea
                : null,

            MobTemplate m =>
                !string.IsNullOrEmpty(m.SpawnRoomBlueprintId)
                    && roomAreaMap.TryGetValue(m.SpawnRoomBlueprintId, out var mArea)
                ? mArea
                : null,

            _ => null,
        };

        private static (string Name, string Description) ExtractNameAndDescription(IEntityTemplate template) => template switch
        {
            AreaTemplate a => (a.Name, a.Description),
            RoomTemplate r => (r.Name, r.Description),
            ItemTemplate i => (i.Name, i.Description),
            MobTemplate m  => (m.Name, m.Description),
            _ => (template.BlueprintId, string.Empty),
        };
    }
}
