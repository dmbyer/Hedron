using System;
using System.Collections.Concurrent;
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
    /// <remarks>
    /// <para>
    /// <strong>Caching.</strong> Reads are served from an in-memory index (per-kind summary lists,
    /// a derived room→area map, and a per-id file-body map). The summary lists and the room→area
    /// map are corpus sweeps; <strong>the per-id map fills one id at a time, on demand</strong>.
    /// That granularity is load-bearing rather than an optimization detail: whole-index
    /// invalidation plus corpus-populated per-id caching would turn
    /// <c>TemplateConformanceSystem.ApplyFlaggedAsync</c>'s and
    /// <c>IAreaLayoutSystem.ApplyProposalAsync</c>'s per-entry <c>Load</c>→write loops into N full
    /// sweeps. With per-id-on-demand population a <c>Load</c> after an invalidation is still one
    /// file read and both loops stay O(N).
    /// </para>
    /// <para>
    /// <strong>The rule that keeps that true, for any caller:</strong> never call
    /// <see cref="List"/> or <see cref="RoomsInArea"/> from inside a loop that also writes. Each
    /// write drops the index, so a summary read in the loop body is a fresh corpus sweep per
    /// iteration. Hoist it above the loop (which is what both callers above do) or reach for
    /// <see cref="Load"/>, which stays one file read.
    /// </para>
    /// <para>
    /// <strong>Invalidation is whole-index.</strong> <c>DeleteAsync</c> clears fields on other
    /// definitions, <c>RenameAsync</c> rewrites every referrer, <c>SaveRoomAsync(bidirectional)</c>
    /// writes an inverse exit on a different room, and the summaries are backed by a derived
    /// room→area map. Entry-scoped invalidation cannot express those cascades, so every write and
    /// every delete drops the whole index.
    /// </para>
    /// <para>
    /// <strong>Concurrency posture (INV-31).</strong> The catalog is a DI singleton reached
    /// concurrently from multiple Blazor circuits. Every mutator is <c>async</c> and invalidates
    /// after an awaited file write, so a thread-affine <c>ReaderWriterLockSlim</c> is unusable (it
    /// cannot be held across an <c>await</c>). Instead the index is a snapshot object swapped under
    /// a plain <c>lock</c>: readers take the current reference with no lock and populate lazily into
    /// it.
    /// </para>
    /// <para>
    /// Lazy population makes every reader a writer, which raises a lost-invalidation hazard: a sweep
    /// that began before a concurrent write must not publish pre-write disk state and leave the
    /// index stale until the next write. <strong>Snapshot identity is the generation</strong> — a
    /// populating reader writes into the snapshot object it captured, and
    /// <see cref="Invalidate"/> detaches that object, so a late publish lands in an orphan nothing
    /// reads. The explicit <c>Generation</c> check in <c>StillCurrent</c> states that invariant
    /// rather than carrying it alone; a design that published into a shared field instead would
    /// depend on the check for correctness.
    /// </para>
    /// <para>
    /// The guard covers <strong>index consistency only</strong>. It does not make YAML writes
    /// atomic; the non-transactional multi-file cascade remains the recorded debt it was. No
    /// live-world component is touched (INV-12/22/23).
    /// </para>
    /// <para>
    /// <strong>The trade that buys.</strong> Rejecting a stale publish means a sustained write loop
    /// (a bulk conformance apply over a large corpus, say) can invalidate faster than a concurrent
    /// reader can publish, so the index stays cold and that reader re-sweeps each time. Deliberate:
    /// correctness over cache retention while writes are in flight. Likewise, two circuits hitting a
    /// cold index concurrently may each sweep the same kind — benign duplicate work, last publish
    /// wins. The "at most one sweep per invalidation" bound is therefore per reader, not global.
    /// </para>
    /// <para>
    /// <strong>Cached bodies, not cached templates.</strong> The per-id map caches the file
    /// <em>text</em> and <c>Load</c> deserializes a fresh template per call. Callers mutate the
    /// template they get back (the editors, and this class's own cascade helpers), so handing out a
    /// shared instance would leak in-progress edits into the index.
    /// </para>
    /// </remarks>
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
        private readonly IContentFileReader _fileReader;
        private readonly string _contentDirectory;
        private readonly string _startingRoomBlueprintId;

        // ── Index state (see the class remarks for the INV-31 posture) ───────────────
        private readonly object _indexLock = new();
        private int _generation;
        private CatalogIndex? _index;

        public ContentDefinitionCatalog(
            IContentSerializer serializer,
            IContentValidator validator,
            ITemplateRegistry templateRegistry,
            IAreaContentWriter areaWriter,
            IRoomContentWriter roomWriter,
            IItemContentWriter itemWriter,
            IMobContentWriter mobWriter,
            IOptions<WorldOptions> options,
            ILogger<ContentDefinitionCatalog> logger,
            IContentFileReader? fileReader = null)
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
                logger,
                fileReader)
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
            ILogger<ContentDefinitionCatalog> logger,
            IContentFileReader? fileReader = null)
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
            _fileReader = fileReader ?? new ContentFileReader();
            _contentDirectory = options.Value.ContentDirectory;
            _startingRoomBlueprintId = options.Value.StartingRoomBlueprintId;
        }

        public IReadOnlyList<ContentSummary> List(ContentKind kind)
        {
            var index = CurrentIndex();
            if (index.Summaries.TryGetValue(kind, out var cached))
                return cached;

            // Rooms carry their own AreaId, so the room sweep needs no map — and the map is then
            // derived from that sweep's result. That ordering is what bounds each kind's directory
            // to at most one sweep per invalidation (Postcondition 3): without it, listing items
            // would sweep the room directory a second time to build the map.
            var roomAreaMap = kind is ContentKind.Item or ContentKind.Mob
                ? EnsureRoomAreaMap(index)
                : EmptyRoomAreaMap;

            var built = SweepSummaries(kind, roomAreaMap);
            PublishSummaries(index, kind, built);
            return built;
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
            var index = CurrentIndex();
            var key = (kind, blueprintId);

            if (!index.Bodies.TryGetValue(key, out var body))
            {
                // Per-id, on-demand fill: one file read, never a corpus sweep.
                var path = DefinitionPath(kind, blueprintId);
                body = _fileReader.FileExists(path) ? _fileReader.ReadAllText(path) : null;
                PublishBody(index, key, body);
            }

            if (body is null)
                return null;

            // Deserialize per call — callers mutate the template they get back.
            var template = _serializer.Deserialize(kind.KindString(), body);
            return new ContentDefinition(kind, template);
        }

        public void Invalidate()
        {
            lock (_indexLock)
            {
                _generation++;
                _index = null;
            }
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

            await WriteTemplateAsync(room, ct).ConfigureAwait(false);

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
                    await WriteTemplateAsync(targetRoom, ct).ConfigureAwait(false);
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
                var applied = await TryApplyCascadeAsync(referrer, blueprintId, newId: null, ct).ConfigureAwait(false);
                if (applied)
                    cascadeEdits.Add(referrer);
            }

            // 3. Delete the target YAML file. (File-only — no EntityService, no SQLite, INV-22/23.)
            var filePath = DefinitionPath(kind, blueprintId);
            DeleteFile(filePath);

            return new ContentDeleteResult(filePath, blueprintId, cascadeEdits);
        }

        public async Task<ContentRenameResult> RenameAsync(
            ContentKind kind,
            string oldId,
            string newId,
            CancellationToken ct = default)
        {
            // 1. Validate newId format (format errors refuse; kind-prefix mismatch is a deferred warning).
            var idReport = _validator.ValidateBlueprintId(kind, newId);
            if (!idReport.IsValid)
                return ContentRenameResult.Failed(oldId, newId, idReport.Errors);

            // 2. Load the target — refuse if it doesn't exist.
            var target = Load(kind, oldId);
            if (target is null)
                return ContentRenameResult.Failed(oldId, newId, new[] { $"{kind} '{oldId}' not found." });

            // 3. Uniqueness — refuse on collision (no merge).
            if (_referenceIndex.Resolves(kind, newId))
            {
                return ContentRenameResult.Failed(oldId, newId, new[]
                {
                    $"A {kind} definition with id '{newId}' already exists.",
                });
            }

            // 4. Build a fresh template carrying newId, copying all state and rewriting the
            // target's own self-referential fields (e.g. a self-loop exit) oldId → newId.
            var newTemplate = CloneWithNewId(kind, target.Template, oldId, newId);
            await WriteDefinitionAsync(new ContentDefinition(kind, newTemplate), ct).ConfigureAwait(false);

            // 5. Cascade-rewrite external referrers. The target's own self-reference (if any) was
            // already handled in step 4 — exclude it here to avoid a redundant load-and-write
            // against the now-stale oldId file.
            var referrers = _referenceIndex.Referrers(kind, oldId);
            var cascadeEdits = new List<ReferrerEdit>();
            foreach (var referrer in referrers)
            {
                if (referrer.ReferrerKind == kind
                    && string.Equals(referrer.ReferrerBlueprintId, oldId, StringComparison.Ordinal))
                {
                    continue;
                }

                var applied = await TryApplyCascadeAsync(referrer, oldId, newId, ct).ConfigureAwait(false);
                if (applied)
                    cascadeEdits.Add(referrer);
            }

            // 6. Delete the old file. (File-only — no EntityService, no SQLite, INV-22/23.)
            var oldPath = DefinitionPath(kind, oldId);
            DeleteFile(oldPath);

            var newPath = DefinitionPath(kind, newId);

            // 7. Fold out-of-YAML advisories + the id-format warning (e.g. kind-prefix mismatch)
            // into the result. Never touches appsettings.json or SQLite (INV-22/23).
            var warnings = new List<string>(idReport.Warnings);
            if (kind == ContentKind.Room)
            {
                warnings.Add(
                    $"Any persistent player/item location referencing room '{oldId}' will not resolve " +
                    $"until the world is reloaded; a parked player recovers via the existing " +
                    $"starting-room fallback in the meantime.");

                if (string.Equals(oldId, _startingRoomBlueprintId, StringComparison.Ordinal))
                {
                    warnings.Add(
                        $"Room '{oldId}' is configured as World:StartingRoomBlueprintId. Update " +
                        $"appsettings.json to '{newId}' manually — rename does not modify configuration.");
                }
            }

            return ContentRenameResult.Ok(oldPath, newPath, oldId, newId, cascadeEdits, warnings);
        }

        public ContentDefinition CreateNew(ContentKind kind, string name) =>
            CreateNew(kind, name, blueprintId: null);

        public ContentDefinition CreateNew(ContentKind kind, string name, string? blueprintId)
        {
            var resolvedId = string.IsNullOrEmpty(blueprintId)
                ? AdhocBlueprintId.Generate(kind.AdhocPrefix(), id => _templateRegistry.TryGet(id, out _))
                : blueprintId;

            IEntityTemplate template = kind switch
            {
                ContentKind.Area => new AreaTemplate(resolvedId) { Name = name },
                ContentKind.Room => new RoomTemplate(resolvedId) { Name = name },
                ContentKind.Item => new ItemTemplate(resolvedId) { Name = name },
                ContentKind.Mob  => new MobTemplate(resolvedId)  { Name = name },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            return new ContentDefinition(kind, template);
        }

        public ContentDefinition WithBlueprintId(ContentDefinition definition, string? blueprintId)
        {
            var resolvedId = string.IsNullOrEmpty(blueprintId)
                ? AdhocBlueprintId.Generate(definition.Kind.AdhocPrefix(), id => _templateRegistry.TryGet(id, out _))
                : blueprintId;

            // Reuses the same clone-and-rewrite rule RenameAsync applies, so there is exactly one
            // id-rewrite rule in the catalog (INV-19).
            var rekeyed = CloneWithNewId(definition.Kind, definition.Template, definition.BlueprintId, resolvedId);
            return new ContentDefinition(definition.Kind, rekeyed);
        }

        public ContentDefinition CreateNextFrom(ContentDefinition previous, string name)
        {
            // Delegates id minting to CreateNew so the slice adds no third id-minting path.
            var next = CreateNew(previous.Kind, name);

            switch (previous.Kind)
            {
                case ContentKind.Area:
                    // Nothing carries forward — areas are authored individually.
                    break;

                case ContentKind.Room:
                    ((RoomTemplate)next.Template).AreaId = ((RoomTemplate)previous.Template).AreaId;
                    break;

                case ContentKind.Item:
                {
                    var from = (ItemTemplate)previous.Template;
                    var to = (ItemTemplate)next.Template;
                    to.Tier = from.Tier;
                    to.Band = from.Band;
                    to.ItemType = from.ItemType;
                    to.WornSlots = new List<WornSlot>(from.WornSlots);
                    break;
                }

                case ContentKind.Mob:
                {
                    var from = (MobTemplate)previous.Template;
                    var to = (MobTemplate)next.Template;
                    to.Tier = from.Tier;
                    to.Band = from.Band;
                    to.SpawnRoomBlueprintId = from.SpawnRoomBlueprintId;
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(previous));
            }

            return next;
        }

        public async Task<ContentWriteResult> CreateAsync(ContentDefinition definition, CancellationToken ct = default)
        {
            var idReport = _validator.ValidateBlueprintId(definition.Kind, definition.BlueprintId);
            if (!idReport.IsValid)
                return ContentWriteResult.Failed(definition.BlueprintId, idReport.Errors);

            if (_referenceIndex.Resolves(definition.Kind, definition.BlueprintId))
            {
                return ContentWriteResult.Failed(definition.BlueprintId, new[]
                {
                    $"A {definition.Kind} definition with id '{definition.BlueprintId}' already exists.",
                });
            }

            var result = await SaveAsync(definition, ct).ConfigureAwait(false);
            if (idReport.Warnings.Count == 0)
                return result;

            var mergedWarnings = new List<string>(idReport.Warnings);
            mergedWarnings.AddRange(result.Warnings);
            return result.Success
                ? ContentWriteResult.OkWithWarnings(result.BlueprintId, mergedWarnings)
                : result;
        }

        public async Task<ContentWriteResult> RemoveRoomExitAsync(
            string roomBlueprintId,
            Direction direction,
            bool bidirectional,
            CancellationToken ct = default)
        {
            var def = Load(ContentKind.Room, roomBlueprintId);
            if (def is null)
                return ContentWriteResult.Failed(roomBlueprintId, new[] { $"Room '{roomBlueprintId}' not found." });

            var room = (RoomTemplate)def.Template;

            if (!room.Exits.Remove(direction, out var targetBlueprintId))
                return ContentWriteResult.Ok(roomBlueprintId);

            var report = _validator.Validate(room);
            if (!report.IsValid)
                return ContentWriteResult.Failed(roomBlueprintId, report.Errors);

            await WriteTemplateAsync(room, ct).ConfigureAwait(false);

            if (bidirectional
                && !string.IsNullOrEmpty(targetBlueprintId)
                && !string.Equals(targetBlueprintId, roomBlueprintId, StringComparison.Ordinal))
            {
                var targetDef = Load(ContentKind.Room, targetBlueprintId);
                if (targetDef is not null)
                {
                    var targetRoom = (RoomTemplate)targetDef.Template;
                    var inverseDir = direction.Opposite();

                    if (targetRoom.Exits.TryGetValue(inverseDir, out var inverseTarget)
                        && string.Equals(inverseTarget, roomBlueprintId, StringComparison.Ordinal))
                    {
                        targetRoom.Exits.Remove(inverseDir);
                        await WriteTemplateAsync(targetRoom, ct).ConfigureAwait(false);
                    }
                }
            }

            return ContentWriteResult.Ok(roomBlueprintId);
        }

        // ── Index ────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> EmptyRoomAreaMap = new(StringComparer.Ordinal);

        /// <summary>
        /// One generation of the in-memory index. The reference is swapped under
        /// <see cref="_indexLock"/>; its contents fill lazily and are therefore concurrent
        /// collections, so a reader that has taken the reference needs no lock.
        /// </summary>
        private sealed class CatalogIndex
        {
            public CatalogIndex(int generation) => Generation = generation;

            public int Generation { get; }

            /// <summary>Per-kind summary list — a corpus sweep of that kind's directory.</summary>
            public ConcurrentDictionary<ContentKind, IReadOnlyList<ContentSummary>> Summaries { get; } = new();

            /// <summary>Per-id raw file body; a <c>null</c> value is a cached "no such file".</summary>
            public ConcurrentDictionary<(ContentKind Kind, string BlueprintId), string?> Bodies { get; } = new();

            /// <summary>Derived room → area map. Assigned and read under <see cref="_indexLock"/>.</summary>
            public Dictionary<string, string>? RoomAreaMap;
        }

        private CatalogIndex CurrentIndex()
        {
            lock (_indexLock)
            {
                return _index ??= new CatalogIndex(_generation);
            }
        }

        /// <summary>
        /// Publishes a lazily populated value only if <paramref name="captured"/> is still the live
        /// generation. Without this check a sweep that began before a concurrent write would
        /// republish pre-write disk state and leave the index stale until the next write
        /// (Postcondition 2) — a race single-threaded invalidation tests cannot catch.
        /// </summary>
        private bool StillCurrent(CatalogIndex captured) =>
            _index is not null && _index.Generation == captured.Generation;

        private void PublishSummaries(CatalogIndex captured, ContentKind kind, IReadOnlyList<ContentSummary> built)
        {
            lock (_indexLock)
            {
                if (StillCurrent(captured))
                    captured.Summaries[kind] = built;
            }
        }

        private void PublishBody(CatalogIndex captured, (ContentKind, string) key, string? body)
        {
            lock (_indexLock)
            {
                if (StillCurrent(captured))
                    captured.Bodies[key] = body;
            }
        }

        /// <summary>
        /// The room blueprintId → AreaId map backing item/mob two-hop area resolution, derived from
        /// the (cached) room summaries rather than a second sweep of the room directory.
        /// </summary>
        private Dictionary<string, string> EnsureRoomAreaMap(CatalogIndex index)
        {
            lock (_indexLock)
            {
                // No StillCurrent check here, deliberately: the caller (List) already holds this
                // same snapshot, so a map read from it is exactly as current as the sweep it feeds,
                // and that sweep's own publish is generation-guarded. Nothing stale can be cached.
                if (index.RoomAreaMap is { } existing)
                    return existing;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var room in List(ContentKind.Room))
            {
                if (!string.IsNullOrEmpty(room.AreaBlueprintId))
                    map[room.BlueprintId] = room.AreaBlueprintId!;
            }

            lock (_indexLock)
            {
                if (StillCurrent(index))
                    index.RoomAreaMap = map;
            }
            return map;
        }

        /// <summary>
        /// One directory sweep for <paramref name="kind"/>: read + deserialize every file. An
        /// unreadable file is logged and skipped rather than failing the listing.
        /// </summary>
        private IReadOnlyList<ContentSummary> SweepSummaries(
            ContentKind kind,
            Dictionary<string, string> roomAreaMap)
        {
            var directory = Path.Combine(_contentDirectory, kind.Subdirectory());
            if (!_fileReader.DirectoryExists(directory))
                return Array.Empty<ContentSummary>();

            var summaries = new List<ContentSummary>();
            foreach (var file in _fileReader.GetFiles(directory, "*" + _serializer.FormatExtension))
            {
                try
                {
                    var body = _fileReader.ReadAllText(file);
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

        // ── Invalidating write primitives ────────────────────────────────────────────
        //
        // Every filesystem mutation the catalog performs goes through one of these two, so no
        // write path can forget to invalidate (Postcondition 2). Invalidation happens *after* the
        // awaited write, which is why the index cannot be guarded by a thread-affine lock.

        private async Task WriteTemplateAsync(IEntityTemplate template, CancellationToken ct)
        {
            switch (template)
            {
                case AreaTemplate area:
                    await _areaWriter.WriteAsync(area, ct).ConfigureAwait(false);
                    break;
                case RoomTemplate room:
                    await _roomWriter.WriteAsync(room, ct).ConfigureAwait(false);
                    break;
                case ItemTemplate item:
                    await _itemWriter.WriteAsync(item, ct).ConfigureAwait(false);
                    break;
                case MobTemplate mob:
                    await _mobWriter.WriteAsync(mob, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(template));
            }

            Invalidate();
        }

        private void DeleteFile(string path)
        {
            // The probe is a read (through the seam, so a test can see it); the delete is a write.
            if (_fileReader.FileExists(path))
                File.Delete(path);

            Invalidate();
        }

        // ── Cascade-apply helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the cascade edit described by <paramref name="referrer"/> and writes the
        /// updated definition via the matching writer. <paramref name="newId"/> <c>null</c> means
        /// clear/remove the referring field (delete's cascade); non-null means rewrite it to the
        /// new id (rename's cascade) — one shared apply serving both verbs (INV-19). Returns
        /// <c>true</c> if the edit was applied and written; <c>false</c> on any error (logs and
        /// continues — best-effort cascade, matching <see cref="DeleteAsync"/>).
        /// </summary>
        private async Task<bool> TryApplyCascadeAsync(
            ReferrerEdit referrer,
            string oldId,
            string? newId,
            CancellationToken ct)
        {
            try
            {
                var def = Load(referrer.ReferrerKind, referrer.ReferrerBlueprintId);
                if (def is null)
                {
                    _logger.LogWarning(
                        "ContentDefinitionCatalog: referrer {Kind} '{Id}' not found on disk; skipping cascade.",
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
                            room.AreaId = newId ?? string.Empty;
                        }
                        else if (referrer.FieldLabel.StartsWith("Exits[", StringComparison.Ordinal))
                        {
                            // Parse the direction from "Exits[North]".
                            var dirName = referrer.FieldLabel["Exits[".Length..^1];
                            if (Enum.TryParse<Direction>(dirName, out var dir))
                            {
                                if (newId is null)
                                    room.Exits.Remove(dir);
                                else
                                    room.Exits[dir] = newId;
                            }
                        }
                        else if (referrer.FieldLabel.StartsWith("SpawnRules[", StringComparison.Ordinal))
                        {
                            if (newId is null)
                            {
                                room.SpawnRules.RemoveAll(r =>
                                    string.Equals(r.BlueprintId, oldId, StringComparison.Ordinal));
                            }
                            else
                            {
                                for (var i = 0; i < room.SpawnRules.Count; i++)
                                {
                                    if (string.Equals(room.SpawnRules[i].BlueprintId, oldId, StringComparison.Ordinal))
                                        room.SpawnRules[i] = room.SpawnRules[i] with { BlueprintId = newId };
                                }
                            }
                        }
                        await WriteTemplateAsync(room, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Item:
                    {
                        var item = (ItemTemplate)def.Template;
                        item.SpawnRoomBlueprintId = newId ?? string.Empty;
                        await WriteTemplateAsync(item, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Mob:
                    {
                        var mob = (MobTemplate)def.Template;
                        mob.SpawnRoomBlueprintId = newId ?? string.Empty;
                        await WriteTemplateAsync(mob, ct).ConfigureAwait(false);
                        return true;
                    }

                    case ContentKind.Area:
                    {
                        var area = (AreaTemplate)def.Template;
                        if (newId is null)
                        {
                            area.Rooms.Remove(oldId);
                        }
                        else
                        {
                            var idx = area.Rooms.IndexOf(oldId);
                            if (idx >= 0)
                                area.Rooms[idx] = newId;
                        }
                        await WriteTemplateAsync(area, ct).ConfigureAwait(false);
                        return true;
                    }

                    default:
                        _logger.LogWarning(
                            "ContentDefinitionCatalog: unhandled referrer kind {Kind}; skipping.",
                            referrer.ReferrerKind);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ContentDefinitionCatalog: failed to apply cascade edit to referrer {Kind} '{Id}'; skipping.",
                    referrer.ReferrerKind, referrer.ReferrerBlueprintId);
                return false;
            }
        }

        /// <summary>
        /// Constructs a fresh template of <paramref name="kind"/> carrying <paramref name="newId"/>,
        /// copying every field from <paramref name="source"/> and rewriting the definition's own
        /// self-referential fields (currently only a room's self-loop exit) <paramref name="oldId"/>
        /// → <paramref name="newId"/>. Template ids are get-only/constructor-set, so rename rebuilds
        /// rather than mutates.
        /// </summary>
        private static IEntityTemplate CloneWithNewId(ContentKind kind, IEntityTemplate source, string oldId, string newId)
        {
            switch (kind)
            {
                case ContentKind.Area:
                {
                    var a = (AreaTemplate)source;
                    var clone = new AreaTemplate(newId)
                    {
                        SchemaVersion = a.SchemaVersion,
                        AreaId = a.AreaId,
                        Name = a.Name,
                        Description = a.Description,
                        RespawnRate = a.RespawnRate,
                        Pvp = a.Pvp,
                        AspectAffinities = a.AspectAffinities is null ? null : new(a.AspectAffinities),
                    };
                    clone.Rooms.AddRange(a.Rooms);
                    return clone;
                }

                case ContentKind.Room:
                {
                    var r = (RoomTemplate)source;
                    var clone = new RoomTemplate(newId)
                    {
                        SchemaVersion = r.SchemaVersion,
                        Name = r.Name,
                        Description = r.Description,
                        AreaId = r.AreaId,
                        X = r.X,
                        Y = r.Y,
                        Z = r.Z,
                    };
                    foreach (var (dir, target) in r.Exits)
                    {
                        clone.Exits[dir] = string.Equals(target, oldId, StringComparison.Ordinal) ? newId : target;
                    }
                    clone.SpawnRules.AddRange(r.SpawnRules);
                    return clone;
                }

                case ContentKind.Item:
                {
                    var i = (ItemTemplate)source;
                    return new ItemTemplate(newId)
                    {
                        Name = i.Name,
                        Description = i.Description,
                        Keywords = new(i.Keywords),
                        ItemType = i.ItemType,
                        WornSlots = new(i.WornSlots),
                        SpawnRoomBlueprintId = i.SpawnRoomBlueprintId,
                        StatBonuses = new(i.StatBonuses),
                        Value = i.Value,
                        Tier = i.Tier,
                        Band = i.Band,
                    };
                }

                case ContentKind.Mob:
                {
                    var m = (MobTemplate)source;
                    return new MobTemplate(newId)
                    {
                        Name = m.Name,
                        Description = m.Description,
                        Keywords = new(m.Keywords),
                        MobType = m.MobType,
                        SpawnRoomBlueprintId = m.SpawnRoomBlueprintId,
                        Level = m.Level,
                        MaxHp = m.MaxHp,
                        Mind = m.Mind,
                        Body = m.Body,
                        Spirit = m.Spirit,
                        Attunement = m.Attunement,
                        MaxMana = m.MaxMana,
                        MaxStamina = m.MaxStamina,
                        MaxAstra = m.MaxAstra,
                        CurrencyLoot = new(m.CurrencyLoot),
                        Protection = m.Protection,
                        Tier = m.Tier,
                        Band = m.Band,
                        IsShop = m.IsShop,
                        ShopAcceptedCurrency = m.ShopAcceptedCurrency,
                        ShopTillSeed = m.ShopTillSeed,
                        ShopRatioOverride = m.ShopRatioOverride,
                        ShopBaseStock = new(m.ShopBaseStock),
                    };
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatches the appropriate writer for a definition (all kinds except Room with
        /// bidirectional). For Room with bidirectional, use <see cref="SaveRoomAsync"/> instead.
        /// </summary>
        private Task WriteDefinitionAsync(ContentDefinition definition, CancellationToken ct) =>
            WriteTemplateAsync(definition.Template, ct);

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
