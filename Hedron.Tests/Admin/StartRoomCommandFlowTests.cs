using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Admin
{
    /// <summary>
    /// Flow tests for <see cref="StartRoomCommand"/> over a temp content directory with the real
    /// loader, manifest store and <see cref="WorldConfiguration"/>: <c>startroom here</c> writes
    /// <c>world.yaml</c>, swaps the live snapshot, and publishes <see cref="StartingRoomSetByAdminEvent"/>;
    /// refusals change nothing.
    /// </summary>
    public sealed class StartRoomCommandFlowTests : IDisposable
    {
        private readonly string _tempDir;

        public StartRoomCommandFlowTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-startroomcmd-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempDir, "rooms"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        // ── Stubs ────────────────────────────────────────────────────────────────

        private sealed class PlaintextHasher : IPasswordHasher
        {
            public string Hash(string password) => "plain:" + password;
            public bool Verify(string password, string hash) => hash == "plain:" + password;
        }

        private sealed class NullAbilitySystem : IAbilitySystem
        {
            public bool Learn(uint entityId, string abilityId) => true;
            public bool Teach(uint t, uint s, string a) => false;
            public bool IsKnown(uint e, string a) => false;
            public bool IsOffensive(string a) => false;
            public IReadOnlyList<string> GetKnown(uint e) => Array.Empty<string>();
            public float GetCooldownRemaining(uint e, string a) => 0f;
            public IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint e)
                => Array.Empty<(string, float)>();
            public void AdvanceCooldowns(TimeSpan elapsed) { }
            public AbilityActivationResult Activate(uint a, string id, uint? t = null, bool ext = false)
                => new(AbilityActivationOutcome.UnknownAbility, id,
                       Array.Empty<Hedron.Core.Modules.Effects.Effect>(),
                       Array.Empty<ResourceCost>(),
                       0f);
        }

        // ── World ────────────────────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; } = new();
            public WorldConfiguration Config { get; } = new();
            public RecordingEventBus Bus { get; } = new(dispatch: false);
            public RecordingOutput Output { get; } = new();
            public WorldManifestStore Store { get; }
            public WorldContentLoader Loader { get; }
            public StartRoomCommand Command { get; }
            public AccountSystem Accounts { get; }

            public TestWorld(string contentDir)
            {
                var options = Options.Create(new WorldOptions
                {
                    ContentDirectory = contentDir,
                    StartingRoomBlueprintId = "room.a",
                });
                var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
                {
                    new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                });
                Store = new WorldManifestStore(options);
                Loader = new WorldContentLoader(
                    Ecs, new TemplateRegistry(Ecs), serializer, new RoomContentWriter(options),
                    Store, Config, options, NullLogger<WorldContentLoader>.Instance);
                Command = new StartRoomCommand(Ecs, Store, Loader, Config, Bus);
                Accounts = new AccountSystem(
                    Ecs, new PlaintextHasher(), Config, Options.Create(new CharacterDefaultsOptions()),
                    new NullAbilitySystem(), TestAttributes.Create(Ecs), new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    NullLogger<AccountSystem>.Instance);
            }

            public uint RoomEntity(string blueprintId) =>
                Ecs.GetAllComponents<RoomComponent>()
                   .Select(p => p.Item1)
                   .Single(id => Ecs.Get<BlueprintComponent>(id).BlueprintId == blueprintId);

            public uint CreateAdminIn(uint roomEntityId)
            {
                var id = Ecs.CreateEntity().Id;
                Ecs.AddComponent(id, new LocationComponent { RoomEntityId = roomEntityId });
                return id;
            }

            public Task RunAsync(uint invoker, string? mode)
            {
                var ctor = typeof(ParsedArguments).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
                    types: new[] { typeof(IReadOnlyDictionary<string, object?>) }, modifiers: null)!;
                var values = new Dictionary<string, object?>();
                if (mode is not null) values["mode"] = mode;
                var args = (ParsedArguments)ctor.Invoke(new object[] { values });
                var context = new CommandContext(
                    new StubSession(invoker), invoker, args, Output.WriterFor(invoker), Services: null!);
                return Command.ExecuteAsync(context);
            }

            public PlainMessage LastMessage(uint invoker) =>
                (PlainMessage)Output.All.Last(m => m.RecipientEntityId == invoker).Message;
        }

        private void WriteRoom(string id) =>
            File.WriteAllText(Path.Combine(_tempDir, "rooms", $"{id}.yaml"),
                $"id: {id}\nname: {id}\ndescription: A test room.\n");

        private string ManifestPath => Path.Combine(_tempDir, "world.yaml");

        private async Task<TestWorld> BootAsync()
        {
            WriteRoom("room.a");
            WriteRoom("room.x");
            var world = new TestWorld(_tempDir);
            await world.Loader.LoadAndSpawnAsync();
            return world;
        }

        // ── Structure ────────────────────────────────────────────────────────────

        [Fact]
        public void StartRoomCommand_requires_admin()
        {
            var cmd = new StartRoomCommand(null!, null!, null!, null!, null!);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
            Assert.Equal(CommandCategory.Admin, cmd.Category);
        }

        [Fact]
        public void StartRoomCommand_takes_no_persistence_dependency()
        {
            // "No SaveEntityAsync": the command cannot save an entity — it has no IPersistenceSystem.
            var parameters = typeof(StartRoomCommand).GetConstructors().Single().GetParameters();
            Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(IPersistenceSystem));
        }

        // ── T7: startroom here ───────────────────────────────────────────────────

        [Fact]
        public async Task Startroom_here_declares_the_room_live_and_durably()
        {
            var world = await BootAsync();
            Assert.Equal(StartingRoomSource.Configuration, world.Config.StartingRoom.Source);

            var existingAccount = await world.Accounts.CreateAccountAsync("old", "pw");
            var existingChar = await world.Accounts.CreateCharacterAsync(existingAccount, "Oldtimer");

            File.WriteAllText(ManifestPath, "schemaVersion: 1\n");
            var x = world.RoomEntity("room.x");
            var admin = world.CreateAdminIn(x);

            await world.RunAsync(admin, "here");

            // Durable: world.yaml declares X, other fields preserved.
            var read = world.Store.Read();
            Assert.Equal("room.x", read.Manifest.StartingRoomId);
            Assert.Equal(1, read.Manifest.SchemaVersion);

            // Live: the snapshot is now the manifest's X.
            Assert.Equal(StartingRoomSource.Manifest, world.Config.StartingRoom.Source);
            Assert.Equal(x, world.Config.StartingRoom.EntityId);

            // Audit event with the previous resolved id.
            var e = Assert.IsType<StartingRoomSetByAdminEvent>(Assert.Single(world.Bus.Published));
            Assert.Equal(admin, e.AdminEntityId);
            Assert.Equal(x, e.RoomEntityId);
            Assert.Equal("room.x", e.RoomBlueprintId);
            Assert.Equal("room.a", e.PreviousStartingRoomId);
            Assert.Equal(OutputSeverity.Confirmation, world.LastMessage(admin).Severity);

            // A character created afterwards lands at X with respawn X; an existing one is untouched.
            var newAccount = await world.Accounts.CreateAccountAsync("new", "pw");
            var newChar = await world.Accounts.CreateCharacterAsync(newAccount, "Newcomer");
            Assert.Equal(x, world.Ecs.Get<LocationComponent>(newChar).RoomEntityId);
            Assert.Equal("room.x", world.Ecs.Get<RespawnComponent>(newChar).RoomBlueprintId);
            Assert.Equal("room.a", world.Ecs.Get<RespawnComponent>(existingChar).RoomBlueprintId);
        }

        // ── T8: refusals ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Startroom_here_outside_a_room_refuses_and_changes_nothing()
        {
            var world = await BootAsync();
            var before = world.Config.StartingRoom;
            var admin = world.CreateAdminIn(9999u);

            await world.RunAsync(admin, "here");

            Assert.Equal(OutputSeverity.Error, world.LastMessage(admin).Severity);
            Assert.False(File.Exists(ManifestPath));
            Assert.Same(before, world.Config.StartingRoom);
            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Startroom_here_with_malformed_manifest_refuses_and_leaves_file_byte_identical()
        {
            var world = await BootAsync();
            File.WriteAllText(ManifestPath, "startingRoomId: [unclosed\n  : : :");
            var bytes = File.ReadAllBytes(ManifestPath);
            var before = world.Config.StartingRoom;
            var admin = world.CreateAdminIn(world.RoomEntity("room.x"));

            await world.RunAsync(admin, "here");

            Assert.Equal(OutputSeverity.Error, world.LastMessage(admin).Severity);
            Assert.Equal(bytes, File.ReadAllBytes(ManifestPath));
            Assert.Same(before, world.Config.StartingRoom);
            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Startroom_with_unknown_token_writes_usage_and_changes_nothing()
        {
            var world = await BootAsync();
            var before = world.Config.StartingRoom;
            var admin = world.CreateAdminIn(world.RoomEntity("room.x"));

            await world.RunAsync(admin, "bogus");

            Assert.Equal(OutputSeverity.Error, world.LastMessage(admin).Severity);
            Assert.False(File.Exists(ManifestPath));
            Assert.Same(before, world.Config.StartingRoom);
            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Startroom_without_argument_is_read_only()
        {
            var world = await BootAsync();
            var before = world.Config.StartingRoom;
            var admin = world.CreateAdminIn(world.RoomEntity("room.x"));

            await world.RunAsync(admin, null);

            Assert.Equal(OutputSeverity.System, world.LastMessage(admin).Severity);
            Assert.False(File.Exists(ManifestPath));
            Assert.Same(before, world.Config.StartingRoom);
            Assert.Empty(world.Bus.Published);
        }
    }
}
