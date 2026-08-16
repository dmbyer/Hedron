using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Commands;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Commands;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Commands;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 3 — flow test for the mob-death → combat-XP-award → contribute-on-read path
    /// (flow-31). Wires real systems + handlers + a dispatching bus with seeded <c>IRandom</c>,
    /// kills a mob, and asserts the Main-Flow postconditions end-to-end.
    ///
    /// Modelled on <see cref="Hedron.Tests.Modules.Economy.CurrencyLootFlowTests"/>.
    /// </summary>
    public sealed class ProgressionAwardFlowTests
    {
        private sealed class NoOpDeathSystem : IDeathSystem
        {
            public DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp)
                => DeathTransition.None;

            public void Respawn(uint entityId) { }

            public bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason)
            {
                failReason = null;
                return true;
            }
        }

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public IStatSystem Stats { get; }
            public IProgressionSystem Progression { get; }
            public CombatSystem Combat { get; }
            public EntityStateService EntityState { get; }
            public RecordingEventBus Bus { get; }
            public RecordingBroadcastSystem Broadcast { get; private set; } = null!;
            public IPreferenceSystem Preferences { get; private set; } = null!;

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();

                Progression = new ProgressionSystem(
                    Ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default), new AdvancementRuleRegistry());
                var contributor = new ProgressionEffectContributor(Progression);
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { contributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(Ecs, effects, deathOpts);
                Stats = new StatSystem(attributes, effects);

                var aspects = new AspectSystem(Ecs);
                Combat = new CombatSystem(Ecs, Stats, attributes, aspects, rng);
                EntityState = new EntityStateService(Ecs);

                Bus = new RecordingEventBus(dispatch: true);

                var deathSystem = new NoOpDeathSystem();
                var tickHandler = new CombatTickHandler(Ecs, Combat, EntityState, deathSystem, Stats, Bus, NullLogger<CombatTickHandler>.Instance);
                var mobDeathHandler = new CombatMobDeathHandler(Ecs, EntityState, Bus);
                Broadcast = new RecordingBroadcastSystem();
                Preferences = new PreferenceSystem(Ecs);
                var advancement = new AdvancementHandler(Progression, new AbilityRegistry(), Bus);
                var narration = new ProgressionNarrationHandler(Preferences, Broadcast);

                Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(tickHandler);
                Bus.Subscribe<CombatEndedEvent>(mobDeathHandler);
                Bus.Subscribe<Hedron.Core.Modules.Mobs.Events.MobDiedEvent>(advancement);
                Bus.Subscribe<CombatRoundEvent>(advancement);
                Bus.Subscribe<Hedron.Core.Modules.Abilities.Events.AbilityActivatedEvent>(advancement);
                Bus.Subscribe<Hedron.Core.Modules.Progression.Events.ExperienceAwardedEvent>(narration);
                Bus.Subscribe<Hedron.Core.Modules.Progression.Events.TrackImprovedEvent>(narration);
            }
        }

        [Fact]
        public async Task MobDeath_awards_combat_experience_and_folds_into_effective_score()
        {
            // combat: hit=20, damage=1 (kills 1-HP mob); progression: Body base=12, HpMax base=12
            // (killer/victim both power 40 → peer scale 1.0, full base each).
            var rng = new FakeRandom(20, 1, 12, 12);
            var world = new TestWorld(rng);

            const uint roomId = 1u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();

            var bodyBefore = world.Stats.Get(playerId, ScoreId.Body);

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            Assert.Equal(12, world.Progression.GetXp(playerId, ScoreId.Body));
            Assert.Equal(12, world.Progression.GetXp(playerId, ScoreId.HpMax));

            var awarded = world.Bus.Published.OfType<Hedron.Core.Modules.Progression.Events.ExperienceAwardedEvent>().ToList();
            Assert.Equal(2, awarded.Count);
            Assert.All(awarded, e => Assert.Equal(playerId, e.EntityId));

            // No threshold crossed at XP 12 (threshold is 100) — effective score unchanged.
            Assert.Equal(bodyBefore, world.Stats.Get(playerId, ScoreId.Body));

            // progress command reflects the accrued XP without prose assertions.
            var output = new RecordingOutput();
            var progressCommand = new ProgressCommand(world.Progression);
            var context = new CommandContext(
                new StubSession(playerId), playerId, ParsedArguments.Empty, output.WriterFor(playerId), null!);
            await progressCommand.ExecuteAsync(context);

            var message = Assert.Single(output.All.Where(r => r.MessageType == typeof(ProgressDisplayMessage)));
            var progressMessage = Assert.IsType<ProgressDisplayMessage>(message.Message);
            Assert.Contains(progressMessage.Rows, r => r.Track == ProgressionTrack.Of(ScoreId.Body) && r.CumulativeXp == 12);
            Assert.Contains(progressMessage.Rows, r => r.Track == ProgressionTrack.Of(ScoreId.HpMax) && r.CumulativeXp == 12);
        }

        [Fact]
        public async Task MobDeath_threshold_crossing_raises_effective_score_via_contributor()
        {
            // Enough combat kills to cross the first Body threshold (100) via repeated 12-XP awards.
            // Each kill draws 4 ints (combat hit+damage, progression Body+HpMax base); 9 kills
            // (9 * 12 = 108 >= 100) comfortably cross the threshold — queue extra sets as a margin.
            var perKill = new[] { 20, 1, 12, 12 };
            var rngValues = Enumerable.Repeat(perKill, 15).SelectMany(x => x).ToArray();
            var rng = new FakeRandom(rngValues);
            var world = new TestWorld(rng);

            const uint roomId = 2u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            uint MakeMob()
            {
                var id = new EntityBuilder(world.Ecs)
                    .AsMob("rat")
                    .WithAttributes(body: 10)
                    .WithPools(hp: 1)
                    .InRoom(roomId)
                    .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                    .Build();
                return id;
            }

            var tick = 1u;
            while (world.Progression.GetImprovementCount(playerId, ScoreId.Body) == 0 && tick < 20)
            {
                var mobId = MakeMob();
                world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
                world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
                world.Combat.StartCombat(playerId, mobId);
                await world.Bus.PublishAsync(Ticks.At(tick));
                tick++;
            }

            Assert.Equal(1, world.Progression.GetImprovementCount(playerId, ScoreId.Body));
            Assert.Equal(10 + ProgressionConstants.PowerPerImprovement, world.Stats.Get(playerId, ScoreId.Body));

            var improved = world.Bus.Published.OfType<Hedron.Core.Modules.Progression.Events.TrackImprovedEvent>()
                .Where(e => e.Track == ProgressionTrack.Of(ScoreId.Body)).ToList();
            Assert.Single(improved);
        }

        // ── Use-based sources (progression-use-based-xp) ──────────────────────────

        /// <summary>
        /// Postconditions 1 and 6: repeated ability use accrues XP on the ability's own track,
        /// crosses a threshold, and the new rank is what <c>skills</c> renders.
        /// </summary>
        [Fact]
        public async Task Ability_use_accrues_the_ability_track_and_skills_shows_the_new_rank()
        {
            // Ability use draws one NextDouble per candidate then one Next(3,7) on a pass. A seeded
            // FakeRandom supplies both, so the loop runs until the ability track improves.
            var world = new TestWorld(new FakeRandom(seed: 11));

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(50u)
                .With(new AbilitiesComponent { Known = { "kick" } })
                .Build();

            var abilityTrack = ProgressionTrack.Ability("kick");

            for (var i = 0; i < 500 && world.Progression.GetImprovementCount(playerId, abilityTrack) == 0; i++)
            {
                await world.Bus.PublishAsync(
                    new Hedron.Core.Modules.Abilities.Events.AbilityActivatedEvent(playerId, "kick", null));
            }

            Assert.True(world.Progression.GetImprovementCount(playerId, abilityTrack) >= 1,
                "Repeated ability use must eventually cross the ability track's first threshold.");

            // kick declares XpAttributeTrack = Body, so the attribute track accrued too.
            Assert.True(world.Progression.GetXp(playerId, ScoreId.Body) > 0);

            // The rank is visible through the `skills` verb.
            var output = new RecordingOutput();
            var skills = new SkillsCommand(
                new AbilitySystem(
                    world.Ecs,
                    new AbilityRegistry(),
                    new EffectSystem(world.Ecs, System.Array.Empty<IEffectContributor>()),
                    new Hedron.Core.Modules.Effects.EffectRegistry(),
                    new AttributeSystem(world.Ecs,
                        new EffectSystem(world.Ecs, System.Array.Empty<IEffectContributor>()),
                        Options.Create(new DeathOptions { HpFloor = -10 })),
                    world.EntityState),
                new AbilityRegistry(),
                world.Progression);

            await skills.ExecuteAsync(new CommandContext(
                new StubSession(playerId), playerId, ParsedArguments.Empty, output.WriterFor(playerId), null!));

            var expectedRank = world.Progression.GetImprovementCount(playerId, abilityTrack);
            Assert.Contains(output.All, r =>
                r.Message is PlainMessage plain && plain.Text.Contains($"rank {expectedRank}"));
        }

        /// <summary>Postcondition 2: damage accrues the <b>defender's</b> tracks, never the attacker's.</summary>
        [Fact]
        public async Task Damage_taken_accrues_the_defender_and_not_the_attacker()
        {
            var world = new TestWorld(new FakeRandom(seed: 3));

            var defenderId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(60u).Build();
            var attackerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(60u).Build();

            for (var i = 0; i < 500 && world.Progression.GetXp(defenderId, ScoreId.Body) == 0; i++)
            {
                await world.Bus.PublishAsync(new CombatRoundEvent(
                    attackerId, defenderId, 60u,
                    new CombatRoundResult(attackerId, defenderId, DamageDealt: 6, AttackerHit: true, CombatRoundOutcome.Hit)));
            }

            Assert.True(world.Progression.GetXp(defenderId, ScoreId.Body) > 0,
                "The defender must accrue XP from taking damage.");
            Assert.Equal(0, world.Progression.GetXp(attackerId, ScoreId.Body));
            Assert.Empty(world.Progression.GetTrackedTracks(attackerId));
        }

        /// <summary>
        /// Postcondition 5: turning the preference off silences the narration without touching the
        /// underlying accrual — the player still progresses, they just stop being told about it.
        /// </summary>
        [Fact]
        public async Task Turning_the_xp_preference_off_silences_narration_but_xp_still_accrues()
        {
            var world = new TestWorld(new FakeRandom(20, 1, 12, 12));

            const uint roomId = 70u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();
            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat").WithAttributes(body: 10).WithPools(hp: 1).InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();

            // config progressionxp off
            var configOutput = new RecordingOutput();
            var config = new ConfigCommand(world.Preferences, world.Bus);
            await config.ExecuteAsync(new CommandContext(
                new StubSession(playerId), playerId,
                Args(("name", "progressionxp"), ("state", "off")),
                configOutput.WriterFor(playerId), null!));

            Assert.False(world.Preferences.IsEnabled(playerId, PreferenceId.ProgressionXpMessages));

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);
            await world.Bus.PublishAsync(Ticks.At(1));

            Assert.Equal(12, world.Progression.GetXp(playerId, ScoreId.Body));
            Assert.Empty(world.Broadcast.ToEntity);
        }

        /// <summary>Postcondition 8: <c>config</c> lists every registered preference with its state.</summary>
        [Fact]
        public async Task Bare_config_lists_every_registered_preference()
        {
            var world = new TestWorld(new FakeRandom(seed: 1));
            var playerId = new EntityBuilder(world.Ecs).AsPlayer().InRoom(80u).Build();

            var output = new RecordingOutput();
            var config = new ConfigCommand(world.Preferences, world.Bus);
            await config.ExecuteAsync(new CommandContext(
                new StubSession(playerId), playerId, ParsedArguments.Empty, output.WriterFor(playerId), null!));

            var message = Assert.IsType<PreferenceListMessage>(
                Assert.Single(output.All.Where(r => r.MessageType == typeof(PreferenceListMessage))).Message);
            Assert.Equal(PreferenceRegistry.All.Count, message.States.Count);
        }

        /// <summary>
        /// Builds a <see cref="ParsedArguments"/> for command tests. Mirrors the reflection-based
        /// construction used by the Ascension flow tests — <c>ParsedArguments</c> has no public
        /// constructor taking a bag.
        /// </summary>
        private static ParsedArguments Args(params (string Key, object Value)[] values)
        {
            var ctor = typeof(ParsedArguments).GetConstructors(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)[0];

            var bag = new System.Collections.Generic.Dictionary<string, object?>();
            foreach (var (key, value) in values)
                bag[key] = value;

            return (ParsedArguments)ctor.Invoke(new object[] { bag });
        }
    }
}
