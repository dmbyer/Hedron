using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Commands;
using Hedron.Core.Modules.Ascension.Events;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Ascension
{
    /// <summary>
    /// Tier 2 — handler/orchestration tests for <see cref="AscendCommand"/>.
    ///
    /// Coverage contract: Events-fired + admin-boundary-save postconditions from
    /// docs/implementation-plans/ascension.md (WP-2, Tier 2).
    /// </summary>
    public sealed class AscendCommandTests
    {
        private sealed class FakeSessionManager : ISessionManager
        {
            private readonly List<ISession> _sessions = new();

            public void Register(ISession session) => _sessions.Add(session);
            public void Unregister(uint playerEntityId) =>
                _sessions.RemoveAll(s => s.PlayerEntityId == playerEntityId);
            public ISession? GetSession(uint playerEntityId) =>
                _sessions.FirstOrDefault(s => s.PlayerEntityId == playerEntityId);
            public IReadOnlyCollection<ISession> GetAll() => _sessions.AsReadOnly();
        }

        private sealed class RecordingPersistence : IPersistenceSystem
        {
            public List<uint> SavedEntityIds { get; } = new();

            public Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
            {
                SavedEntityIds.Add(entityId);
                return Task.CompletedTask;
            }

            public Task FlushAllAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task FlushDirtyAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<uint>>(Array.Empty<uint>());
        }

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public IAscensionSystem Ascension { get; }
            public FakeSessionManager Sessions { get; }
            public RecordingPersistence Persistence { get; }
            public RecordingEventBus Bus { get; }
            public AscendCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Ascension = new AscensionSystem(Ecs);
                Sessions = new FakeSessionManager();
                Persistence = new RecordingPersistence();
                Bus = new RecordingEventBus(dispatch: false);
                Command = new AscendCommand(Ascension, Sessions, Ecs, Bus, Persistence);
            }

            public uint CreatePlayer(string characterName)
            {
                var entityId = Ecs.CreateEntity().Id;
                Ecs.AddComponent(entityId, new CharacterComponent { CharacterName = characterName });
                Sessions.Register(new StubSession(entityId));
                return entityId;
            }
        }

        private static ParsedArguments MakeArgs(string? characterName)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;

            var values = new Dictionary<string, object?>();
            if (characterName is not null)
                values["characterName"] = characterName;

            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(uint invokerEntityId, ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(invokerEntityId);
            return new CommandContext(
                session, invokerEntityId, args, output.WriterFor(invokerEntityId), Services: null!);
        }

        [Fact]
        public void RequiredPrivileges_contains_AdminRequirement()
        {
            var cmd = new AscendCommand(null!, null!, null!, null!, null!);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
        }

        [Fact]
        public async Task ExecuteAsync_on_success_publishes_one_AscendedEvent_and_one_audit_event_and_saves_once()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Alice");
            const uint adminId = 99u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs("Alice"), output);

            await world.Command.ExecuteAsync(ctx);

            var ascended = world.Bus.Published.OfType<AscendedEvent>().ToList();
            Assert.Single(ascended);
            Assert.Equal(playerId, ascended[0].EntityId);
            Assert.Equal(1, ascended[0].NewTier);
            Assert.Equal(0, ascended[0].PreviousTier);

            var audit = world.Bus.Published.OfType<PlayerAscendedByAdminEvent>().ToList();
            Assert.Single(audit);
            Assert.Equal(adminId, audit[0].AdminEntityId);
            Assert.Equal(playerId, audit[0].TargetEntityId);
            Assert.Equal(1, audit[0].NewTier);

            Assert.Single(world.Persistence.SavedEntityIds);
            Assert.Equal(playerId, world.Persistence.SavedEntityIds[0]);
        }

        [Fact]
        public async Task ExecuteAsync_omitted_characterName_ascends_the_invoker()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Self");

            var output = new RecordingOutput();
            var ctx = MakeContext(playerId, MakeArgs(null), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(1, world.Ascension.GetTier(playerId));
        }

        [Fact]
        public async Task ExecuteAsync_at_max_tier_publishes_nothing_and_saves_nothing()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("MaxedOut");
            for (var i = 0; i < AscensionConstants.MaxTier; i++)
                world.Ascension.TryAscend(playerId);

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("MaxedOut"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.Bus.Published.OfType<AscendedEvent>());
            Assert.Empty(world.Bus.Published.OfType<PlayerAscendedByAdminEvent>());
            Assert.Empty(world.Persistence.SavedEntityIds);
            Assert.Equal(AscensionConstants.MaxTier, world.Ascension.GetTier(playerId));
        }

        [Fact]
        public async Task ExecuteAsync_no_mutation_when_player_not_found()
        {
            var world = new TestWorld();

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Nobody"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.Bus.Published);
            Assert.Empty(world.Persistence.SavedEntityIds);
        }
    }
}
