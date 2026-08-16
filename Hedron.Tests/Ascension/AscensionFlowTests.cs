using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Commands;
using Hedron.Core.Modules.Ascension.Events;
using Hedron.Core.Modules.Ascension.Handlers;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Ascension
{
    /// <summary>
    /// Tier 3 — Main-Flow executable for the ascension journey (flow-32): admin <c>ascend</c> →
    /// tier increments → the baseline folds into <see cref="IStatSystem.Get"/> → <see cref="AscendedEvent"/>
    /// published. Embeds the functional-validation gate: a Tier-1-banded mob out-scales a Tier-0
    /// fixture (deadly) pre-ascend; the same mob normalizes (medium) post-ascend — the deadly→medium
    /// demonstration via the baseline delta, no power-budget oracle needed (deferred to prog-3).
    ///
    /// Real <see cref="IAscensionSystem"/> + <see cref="AscendCommand"/> + dispatching bus + fake persistence.
    /// </summary>
    public sealed class AscensionFlowTests
    {
        private sealed class FakeSessionManager : ISessionManager
        {
            private readonly List<ISession> _sessions = new();
            public void Register(ISession session) => _sessions.Add(session);
            public void Unregister(uint playerEntityId) => _sessions.RemoveAll(s => s.PlayerEntityId == playerEntityId);
            public ISession? GetSession(uint playerEntityId) => _sessions.FirstOrDefault(s => s.PlayerEntityId == playerEntityId);
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
            public IStatSystem Stats { get; }
            public FakeSessionManager Sessions { get; }
            public RecordingPersistence Persistence { get; }
            public RecordingEventBus Bus { get; }
            public AscendCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Ascension = new AscensionSystem(Ecs);

                var contributor = new AscensionEffectContributor(Ascension);
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { contributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(Ecs, effects, deathOpts);
                Stats = new StatSystem(attributes, effects);

                Sessions = new FakeSessionManager();
                Persistence = new RecordingPersistence();
                Bus = new RecordingEventBus(dispatch: true);
                Command = new AscendCommand(Ascension, Sessions, Ecs, Bus, Persistence);

                var narration = new AscensionNarrationHandler(Ecs, new NoOpBroadcastSystem());
                Bus.Subscribe<AscendedEvent>(narration);
            }

            public uint CreatePlayer(string characterName, int body)
            {
                var entityId = new EntityBuilder(Ecs).AsPlayer().WithAttributes(body: body).InRoom(1u).Build();
                Ecs.AddComponent(entityId, new CharacterComponent { CharacterName = characterName });
                Sessions.Register(new StubSession(entityId));
                return entityId;
            }
        }

        // Minimal no-op broadcast so the real AscensionNarrationHandler can run without a session/output stack.
        private sealed class NoOpBroadcastSystem : IBroadcastSystem
        {
            public Task SendToRoomAsync(uint roomEntityId, Hedron.Core.Output.IOutputMessage message, Func<uint, bool>? audienceFilter = null)
                => Task.CompletedTask;
            public Task SendToEntityAsync(uint playerEntityId, Hedron.Core.Output.IOutputMessage message)
                => Task.CompletedTask;
            public Task SendToAllAsync(Hedron.Core.Output.IOutputMessage message)
                => Task.CompletedTask;
            public Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId)
                => Task.CompletedTask;
        }

        private static ParsedArguments MakeArgs(string characterName)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;
            var values = new Dictionary<string, object?> { ["characterName"] = characterName };
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(uint invokerEntityId, ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(invokerEntityId);
            return new CommandContext(session, invokerEntityId, args, output.WriterFor(invokerEntityId), Services: null!);
        }

        [Fact]
        public async Task Admin_ascend_increments_tier_folds_baseline_and_publishes_AscendedEvent()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Hero", body: 10);
            var bodyBefore = world.Stats.Get(playerId, ScoreId.Body);

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Hero"), output);
            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(1, world.Ascension.GetTier(playerId));

            var bodyAfter = world.Stats.Get(playerId, ScoreId.Body);
            Assert.Equal(bodyBefore + AscensionConstants.TierBaselineStep, bodyAfter);

            var events = world.Bus.Published.OfType<AscendedEvent>().ToList();
            Assert.Single(events);
            Assert.Equal(playerId, events[0].EntityId);
            Assert.Equal(1, events[0].NewTier);

            Assert.Single(world.Persistence.SavedEntityIds);
        }

        [Fact]
        public async Task Functional_validation_gate_Tier1_mob_deadly_pre_ascend_medium_post_ascend()
        {
            var world = new TestWorld();
            // Fixture starts at Tier 0, base Body 10.
            var playerId = world.CreatePlayer("Fixture", body: 10);

            // Tier-1-banded mob "tuned" to the Tier-1 baseline stats: base Body equal to the
            // player's base + one baseline step, mirroring the design note "a Tier-N mob is
            // simply tuned to Tier-N baseline stats."
            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("banded-trash")
                .WithAttributes(body: 10 + AscensionConstants.TierBaselineStep)
                .Build();
            world.Ecs.Get<MobDataComponent>(mobId).Tier = 1;

            var playerScore = world.Stats.Get(playerId, ScoreId.Body);
            var mobScore = world.Stats.Get(mobId, ScoreId.Body);
            Assert.Equal(AscensionConstants.TierBaselineStep, mobScore - playerScore); // deadly: full tier gap

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Fixture"), output);
            await world.Command.ExecuteAsync(ctx);

            var playerScoreAfter = world.Stats.Get(playerId, ScoreId.Body);
            var mobScoreAfter = world.Stats.Get(mobId, ScoreId.Body);
            Assert.Equal(0, mobScoreAfter - playerScoreAfter); // medium: gap closed by one baseline step
        }
    }
}
