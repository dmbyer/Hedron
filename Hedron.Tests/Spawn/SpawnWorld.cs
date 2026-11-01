using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Commands;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Commands;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Shopping.Handlers;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Flow-tier harness for spawn-authoring: a temp content directory, the real loader and
    /// deserializers/writers, <see cref="SpawnSystem"/> and <see cref="ShopkeeperSpawnHandler"/>
    /// subscribed to a dispatching <see cref="RecordingEventBus"/> (priority order), and the admin
    /// commands that author spawn rules. Nothing persists — <see cref="Persistence"/> records saves.
    /// </summary>
    internal sealed class SpawnWorld : IDisposable
    {
        public sealed class RecordingPersistence : IPersistenceSystem
        {
            public int Saves;
            public int Flushes;
            public Task SaveEntityAsync(uint entityId, CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
            public Task FlushAllAsync(CancellationToken ct = default) { Flushes++; return Task.CompletedTask; }
            public Task FlushDirtyAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<uint>>(Array.Empty<uint>());
        }

        /// <summary>The admin invoker: a bare entity with a location, no blueprint (survives reload teardown).</summary>
        public uint AdminId { get; }

        public string Dir { get; } = Path.Combine(Path.GetTempPath(), $"hedron-spawnworld-{Guid.NewGuid():N}");
        public EntityService Ecs { get; } = new();
        public TemplateRegistry Registry { get; }
        public FakeClock Clock { get; } = new();
        public RecordingEventBus Bus { get; } = new(dispatch: true);
        public RecordingPersistence Persistence { get; } = new();
        public WorldContentLoader Loader { get; }
        public SpawnSystem Spawn { get; }
        public RoomBuilderSystem RoomBuilder { get; }
        public MobBuilderSystem MobBuilder { get; }
        public ItemBuilderSystem ItemBuilder { get; }
        public RoomContentWriter RoomWriter { get; }
        public AreaContentWriter AreaWriter { get; }
        public AreaPlacementSystem Placement { get; }
        public AreaBuilderSystem AreaBuilder { get; }
        public MobContentWriter MobWriter { get; }
        public ItemContentWriter ItemWriter { get; }
        public RecordingOutput Output { get; } = new();
        public WorldConfiguration Config { get; } = new();

        private readonly IOptions<WorldOptions> _options;
        private readonly YamlContentSerializer _serializer;

        /// <summary>A loader over this world's registry/config with a chosen logger and participants.</summary>
        public WorldContentLoader LoaderWithLogger(
            Microsoft.Extensions.Logging.ILogger<WorldContentLoader> logger,
            IEnumerable<Hedron.Core.Systems.Definitions.IContentReloadParticipant>? participants) =>
            new(Ecs, Registry, _serializer, RoomWriter, new WorldManifestStore(_options), Config, _options, logger, participants);

        public SpawnWorld(IEnumerable<Hedron.Core.Systems.Definitions.IContentReloadParticipant>? participants = null)
        {
            Directory.CreateDirectory(Dir);
            AdminId = Ecs.CreateEntity().Id;
            Registry = new TemplateRegistry(Ecs);
            var options = Options.Create(new WorldOptions { ContentDirectory = Dir, StartingRoomBlueprintId = "room.start" });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });
            RoomWriter = new RoomContentWriter(options);
            AreaWriter = new AreaContentWriter(options);
            MobWriter = new MobContentWriter(options);
            ItemWriter = new ItemContentWriter(options);
            _options = options;
            _serializer = serializer;
            Loader = LoaderWithLogger(NullLogger<WorldContentLoader>.Instance, participants);

            Spawn = new SpawnSystem(Ecs, Registry, Clock, NullLogger<SpawnSystem>.Instance);
            var shopSystem = new ShopSystem(Ecs, new WalletSystem(Ecs), new ItemSystem(Ecs), Clock, Options.Create(new ShopOptions()));
            var shopkeepers = new ShopkeeperSpawnHandler(Ecs, Registry, shopSystem, NullLogger<ShopkeeperSpawnHandler>.Instance);
            Bus.Subscribe<WorldContentReadyEvent>(Spawn);
            Bus.Subscribe<WorldContentReadyEvent>(shopkeepers);
            Bus.Subscribe<Hedron.Core.Modules.Mobs.Events.MobDiedEvent>(Spawn);
            Bus.Subscribe<Hedron.Core.Modules.Items.Events.ItemPickedUpEvent>(Spawn);
            Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(Spawn);

            Placement = new AreaPlacementSystem(Registry);
            RoomBuilder = new RoomBuilderSystem(Ecs, Registry, new AreaSystem(Ecs, Registry), Spawn, Placement, NullLogger<RoomBuilderSystem>.Instance);
            AreaBuilder = new AreaBuilderSystem(Ecs, Registry, NullLogger<AreaBuilderSystem>.Instance);
            MobBuilder = new MobBuilderSystem(Ecs, Registry, TestAttributes.Create(Ecs), NullLogger<MobBuilderSystem>.Instance);
            ItemBuilder = new ItemBuilderSystem(Ecs, Registry, NullLogger<ItemBuilderSystem>.Instance);
        }

        // ── Content files ────────────────────────────────────────────────────────

        public string PathOf(string subdir, string id) => Path.Combine(Dir, subdir, id + ".yaml");

        public void Write(string subdir, string id, string body)
        {
            Directory.CreateDirectory(Path.Combine(Dir, subdir));
            File.WriteAllText(PathOf(subdir, id), body);
        }

        public void Room(string id, string spawnRulesYaml = "", string? areaId = null) =>
            Write("rooms", id, $"id: {id}\nname: {id}\n" + (areaId is null ? "" : $"areaId: {areaId}\n") +
                (spawnRulesYaml.Length > 0 ? "spawnRules:\n" + spawnRulesYaml : ""));

        public void Area(string id, string extra = "") => Write("areas", id, $"id: {id}\nname: {id}\n{extra}");

        public void Mob(string id, string extra = "") => Write("mobs", id, $"blueprintId: {id}\nname: {id}\nlevel: 1\n{extra}");
        public void Item(string id, string extra = "") => Write("items", id, $"blueprintId: {id}\nname: {id}\n{extra}");

        public string Read(string subdir, string id) => File.ReadAllText(PathOf(subdir, id));

        // ── World lifecycle ──────────────────────────────────────────────────────

        public async Task BootAsync()
        {
            await Loader.LoadAndSpawnAsync();
            await Bus.PublishAsync(new WorldContentReadyEvent());
        }

        public uint RoomEntity(string blueprintId) =>
            Ecs.GetAllComponents<RoomComponent>()
                .Select(e => e.EntityId)
                .Single(id => Ecs.Get<BlueprintComponent>(id).BlueprintId == blueprintId);

        public List<uint> InRoom(uint roomId, string blueprintId) =>
            Ecs.GetAllComponents<BlueprintComponent>()
                .Where(e => string.Equals(e.Component.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
                .Where(e => Ecs.TryGet<LocationComponent>(e.EntityId, out var loc) && loc.RoomEntityId == roomId)
                .Select(e => e.EntityId)
                .ToList();

        /// <summary>Places a player-ish admin invoker in <paramref name="roomId"/>.</summary>
        public void StandIn(uint roomId)
        {
            if (Ecs.TryGet<LocationComponent>(AdminId, out var loc))
                loc.RoomEntityId = roomId;
            else
                Ecs.AddComponent(AdminId, new LocationComponent { RoomEntityId = roomId });
        }

        // ── Commands ─────────────────────────────────────────────────────────────

        private SpawnRuleAuthoringOutcome Outcome() => new(Registry, RoomWriter, Bus);

        public SpawnRuleCommand SpawnRuleCommand() =>
            new(Ecs, RoomBuilder, Spawn, Outcome(), Clock);

        public MkMobCommand MkMobCommand() =>
            new(MobBuilder, MobWriter, RoomBuilder, Outcome(), Ecs, Bus);

        public MkitemCommand MkitemCommand() =>
            new(ItemBuilder, ItemWriter, RoomBuilder, Outcome(), Ecs, Bus);

        public SetMobCommand SetMobCommand() => new(MobBuilder, MobWriter, Registry, Bus);
        public SetitemCommand SetitemCommand() => new(ItemBuilder, ItemWriter, Registry, Bus);

        public ReloadCommand ReloadCommand() => new(Loader, Bus, Persistence);

        public AreaSetCommand AreaSetCommand() => new(AreaBuilder, AreaWriter, Bus);
        public AreaCommand AreaCommand() => new(new AreaSystem(Ecs, Registry), Placement, Ecs);

        public Task RunAsync(ICommand command, IReadOnlyDictionary<string, object?> args) =>
            command.ExecuteAsync(new CommandContext(new StubSession(AdminId), AdminId, Args(args), Output.WriterFor(AdminId), Services: null!));

        public static ParsedArguments Args(IReadOnlyDictionary<string, object?> values)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) }, modifiers: null)!;
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        public void Dispose()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
    }
}
