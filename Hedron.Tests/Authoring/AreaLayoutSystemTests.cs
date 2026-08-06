using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// System-unit tests for <see cref="AreaLayoutSystem"/> — the visual grid editor's
    /// deterministic BFS auto-layout proposal and its bulk "Apply layout" write.
    ///
    /// Coverage contract: docs/implementation-plans/world-editor-grid.md Postconditions 7-8.
    /// Each fixture uses a fresh temp content directory with the real catalog/writers/serializer
    /// (no mocks), mirroring <see cref="ContentDefinitionCatalogTests"/>'s harness.
    /// </summary>
    public sealed class AreaLayoutSystemTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private sealed class StubAbilityRegistry
            : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry() : base(Array.Empty<AbilityDefinition>(), d => d.Id) { }
        }

        private sealed class StubEffectRegistry
            : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry() : base(Array.Empty<EffectDefinition>(), d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry
            : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry() : base(Array.Empty<AspectDefinition>(), d => d.Id) { }
        }

        private ContentDefinitionCatalog NewCatalog()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-layout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);

            var options = Options.Create(new WorldOptions { ContentDirectory = dir });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });

            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var validator = new ContentValidator(
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs, registry);

            return new ContentDefinitionCatalog(
                serializer,
                validator,
                registry,
                new AreaContentWriter(options),
                new RoomContentWriter(options),
                new ItemContentWriter(options),
                new MobContentWriter(options),
                options,
                NullLogger<ContentDefinitionCatalog>.Instance);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static async Task<RoomTemplate> MakeRoom(
            ContentDefinitionCatalog catalog, string areaId, string name,
            int? x = null, int? y = null, int? z = null)
        {
            var def = catalog.CreateNew(ContentKind.Room, name);
            var room = (RoomTemplate)def.Template;
            room.AreaId = areaId;
            room.X = x;
            room.Y = y;
            room.Z = z;
            await catalog.SaveAsync(def);
            return room;
        }

        private static async Task Link(ContentDefinitionCatalog catalog, RoomTemplate from, Direction dir, RoomTemplate to)
        {
            from.Exits[dir] = to.BlueprintId;
            await catalog.SaveRoomAsync(from, bidirectional: false);
        }

        // ── Propose: empty / all-anchored ─────────────────────────────────────────────

        [Fact]
        public void Propose_EmptyArea_YieldsEmptyProposal()
        {
            var catalog = NewCatalog();
            var system = new AreaLayoutSystem(catalog);

            var proposal = system.Propose("area.nonexistent");

            Assert.Empty(proposal.Anchored);
            Assert.Empty(proposal.Proposed);
            Assert.Empty(proposal.Collisions);
        }

        [Fact]
        public async Task Propose_AllRoomsAnchored_YieldsEmptyProposedSet()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B", 1, 0, 0);
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Equal(2, proposal.Anchored.Count);
            Assert.Empty(proposal.Proposed);
        }

        // ── Propose: anchors never move ───────────────────────────────────────────────

        [Fact]
        public async Task Propose_NeverMovesAnAnchoredRoom()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 5, 5, 0);
            var b = await MakeRoom(catalog, "area.1", "B"); // coordless
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Equal(new RoomPosition(5, 5, 0), proposal.Anchored[a.BlueprintId]);
        }

        // ── Propose: adjacent placement follows Offset when free ────────────────────

        [Fact]
        public async Task Propose_PlacesCoordlessNeighbor_AtOffsetCell_WhenFree()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B");
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Equal(new RoomPosition(1, 0, 0), proposal.Proposed[b.BlueprintId]);
        }

        [Fact]
        public async Task Propose_UpDownExits_ProposeZPlusOrMinusOne()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var up = await MakeRoom(catalog, "area.1", "Up");
            var down = await MakeRoom(catalog, "area.1", "Down");
            await Link(catalog, a, Direction.Up, up);
            await Link(catalog, a, Direction.Down, down);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Equal(new RoomPosition(0, 0, 1), proposal.Proposed[up.BlueprintId]);
            Assert.Equal(new RoomPosition(0, 0, -1), proposal.Proposed[down.BlueprintId]);
        }

        // ── Propose: occupied-cell spill ──────────────────────────────────────────────

        [Fact]
        public async Task Propose_SpillsToNearestFreeCell_WhenNaturalOffsetIsOccupied()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            // Pre-occupy the natural east cell with another anchor.
            var occupant = await MakeRoom(catalog, "area.1", "Occupant", 1, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B"); // coordless, wants to go east of A
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            var bPos = proposal.Proposed[b.BlueprintId];
            Assert.NotEqual(new RoomPosition(1, 0, 0), bPos);
            Assert.Equal(0, bPos.Z); // spill stays on the same Z as the natural candidate.
        }

        // ── Propose: collision-free proposals ─────────────────────────────────────────

        [Fact]
        public async Task Propose_ProposedCellsNeverCollide_WithAnchorsOrEachOther()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B");
            var c = await MakeRoom(catalog, "area.1", "C");
            var d = await MakeRoom(catalog, "area.1", "D");
            await Link(catalog, a, Direction.East, b);
            await Link(catalog, a, Direction.North, c);
            await Link(catalog, a, Direction.West, d);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            var allCells = proposal.Anchored.Values.Concat(proposal.Proposed.Values).ToList();
            Assert.Equal(allCells.Count, allCells.Distinct().Count());
        }

        // ── Propose: disconnected components get deterministic free origins ─────────

        [Fact]
        public async Task Propose_DisconnectedComponents_GetDistinctDeterministicOrigins()
        {
            var catalog = NewCatalog();
            // Two separate two-room components, no exits linking them, no anchors at all.
            var a1 = await MakeRoom(catalog, "area.1", "A1");
            var a2 = await MakeRoom(catalog, "area.1", "A2");
            await Link(catalog, a1, Direction.East, a2);

            var b1 = await MakeRoom(catalog, "area.1", "B1");
            var b2 = await MakeRoom(catalog, "area.1", "B2");
            await Link(catalog, b1, Direction.East, b2);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Equal(4, proposal.Proposed.Count);
            var cells = proposal.Proposed.Values.ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }

        // ── Propose: determinism ──────────────────────────────────────────────────────

        [Fact]
        public async Task Propose_IsDeterministic_SameDiskStateYieldsSameProposal()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B");
            var c = await MakeRoom(catalog, "area.1", "C");
            await Link(catalog, a, Direction.East, b);
            await Link(catalog, b, Direction.East, c);

            var system = new AreaLayoutSystem(catalog);
            var first = system.Propose("area.1");
            var second = system.Propose("area.1");

            Assert.Equal(first.Proposed, second.Proposed);
            Assert.Equal(first.Anchored, second.Anchored);
        }

        // ── Propose: collisions reported ──────────────────────────────────────────────

        [Fact]
        public async Task Propose_ReportsCollisionsAmongAnchoredRooms()
        {
            var catalog = NewCatalog();
            await MakeRoom(catalog, "area.1", "A", 2, 2, 0);
            await MakeRoom(catalog, "area.1", "B", 2, 2, 0);

            var system = new AreaLayoutSystem(catalog);
            var proposal = system.Propose("area.1");

            Assert.Single(proposal.Collisions);
            Assert.Equal(2, proposal.Collisions[0].RoomBlueprintIds.Count);
        }

        // ── ApplyProposalAsync ────────────────────────────────────────────────────────

        [Fact]
        public async Task ApplyProposalAsync_WritesOnlyPreviouslyCoordlessRooms()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B");
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);
            var result = await system.ApplyProposalAsync("area.1");

            // "area.1" is a bare id with no Area definition on disk in this fixture — the
            // catalog's warn-but-allow dangling-AreaId cross-ref notice is expected noise here,
            // unrelated to the layout apply itself.
            Assert.Equal(1, result.Written);

            var reloadedA = (RoomTemplate)catalog.Load(ContentKind.Room, a.BlueprintId)!.Template;
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, b.BlueprintId)!.Template;
            Assert.Equal((0, 0, 0), (reloadedA.X, reloadedA.Y, reloadedA.Z));
            Assert.Equal((1, 0, 0), (reloadedB.X, reloadedB.Y, reloadedB.Z));
        }

        [Fact]
        public async Task ApplyProposalAsync_ReDerivesFromDisk_ReflectingChangesSincePropose()
        {
            var catalog = NewCatalog();
            var a = await MakeRoom(catalog, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(catalog, "area.1", "B");
            await Link(catalog, a, Direction.East, b);

            var system = new AreaLayoutSystem(catalog);

            // Take a proposal, then mutate disk state before applying — Apply must re-derive,
            // not reuse a cached proposal.
            _ = system.Propose("area.1");
            var c = await MakeRoom(catalog, "area.1", "C");
            await Link(catalog, b, Direction.East, c);

            var result = await system.ApplyProposalAsync("area.1");

            Assert.Equal(2, result.Written); // B and C both written; A stays anchored.
            var reloadedC = (RoomTemplate)catalog.Load(ContentKind.Room, c.BlueprintId)!.Template;
            Assert.NotNull(reloadedC.X);
        }

        [Fact]
        public async Task ApplyProposalAsync_BestEffort_WarnsOnFailingRoom_AndWritesTheRest()
        {
            var inner = NewCatalog();
            var a = await MakeRoom(inner, "area.1", "A", 0, 0, 0);
            var b = await MakeRoom(inner, "area.1", "B");
            var c = await MakeRoom(inner, "area.1", "C");
            await Link(inner, a, Direction.East, b);
            await Link(inner, a, Direction.North, c);

            var wrapped = new FailOnSaveCatalog(inner, failFor: b.BlueprintId);
            var system = new AreaLayoutSystem(wrapped);

            var result = await system.ApplyProposalAsync("area.1");

            Assert.Equal(1, result.Written); // C written; B failed.
            Assert.Contains(result.Warnings, w => w.Contains(b.BlueprintId));

            var reloadedC = (RoomTemplate)inner.Load(ContentKind.Room, c.BlueprintId)!.Template;
            Assert.NotNull(reloadedC.X);
        }

        /// <summary>Decorator that throws from <see cref="SaveRoomAsync"/> for one blueprint id, delegating everything else.</summary>
        private sealed class FailOnSaveCatalog : IContentDefinitionCatalog
        {
            private readonly IContentDefinitionCatalog _inner;
            private readonly string _failFor;

            public FailOnSaveCatalog(IContentDefinitionCatalog inner, string failFor)
            {
                _inner = inner;
                _failFor = failFor;
            }

            public IReadOnlyList<ContentSummary> List(ContentKind kind) => _inner.List(kind);
            public IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId) => _inner.RoomsInArea(areaBlueprintId);
            public ContentDefinition? Load(ContentKind kind, string blueprintId) => _inner.Load(kind, blueprintId);
            public Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default) => _inner.SaveAsync(definition, ct);

            public Task<ContentWriteResult> SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct = default)
            {
                if (room.BlueprintId == _failFor)
                    throw new InvalidOperationException($"Simulated failure for '{_failFor}'.");
                return _inner.SaveRoomAsync(room, bidirectional, ct);
            }

            public Task<ContentDeleteResult> DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct = default) => _inner.DeleteAsync(kind, blueprintId, ct);
            public Task<ContentRenameResult> RenameAsync(ContentKind kind, string oldId, string newId, CancellationToken ct = default) => _inner.RenameAsync(kind, oldId, newId, ct);
            public ContentDefinition CreateNew(ContentKind kind, string name) => _inner.CreateNew(kind, name);
            public ContentDefinition CreateNew(ContentKind kind, string name, string? blueprintId) => _inner.CreateNew(kind, name, blueprintId);
            public Task<ContentWriteResult> CreateAsync(ContentDefinition definition, CancellationToken ct = default) => _inner.CreateAsync(definition, ct);
            public Task<ContentWriteResult> RemoveRoomExitAsync(string roomBlueprintId, Direction direction, bool bidirectional, CancellationToken ct = default) =>
                _inner.RemoveRoomExitAsync(roomBlueprintId, direction, bidirectional, ct);

            public ContentDefinition WithBlueprintId(ContentDefinition definition, string? blueprintId) =>
                _inner.WithBlueprintId(definition, blueprintId);

            public ContentDefinition CreateNextFrom(ContentDefinition previous, string name) =>
                _inner.CreateNextFrom(previous, name);

            public void Invalidate() => _inner.Invalidate();
        }
    }
}
