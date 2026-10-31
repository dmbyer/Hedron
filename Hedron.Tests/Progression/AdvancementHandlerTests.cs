using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 2 — handler / orchestration tests for <see cref="AdvancementHandler"/>.
    ///
    /// Migrated from <c>ExperienceAwardHandlerTests</c> when the per-source handler pattern was
    /// promoted to the advancement-rule registry (INV-19), and extended to the two new trigger
    /// events. Coverage contract: the Events-fired postconditions of
    /// docs/implementation-plans/progression-use-based-xp.md, extended by the relative-power slice
    /// (WP-2) with the per-trigger opponent-ratio gather — every case reads power through a real
    /// <see cref="RelativePowerSystem"/> over a real <see cref="StatSystem"/>.
    /// </summary>
    public sealed class AdvancementHandlerTests
    {
        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public ProgressionSystem Progression { get; }
            public AdvancementHandler Handler { get; }
            public RecordingEventBus Bus { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();
                Progression = new ProgressionSystem(Ecs, rng, new AdvancementRuleRegistry());
                var effects = new EffectSystem(Ecs, new IEffectContributor[]
                {
                    new EquipmentEffectContributor(Ecs),
                    new ProgressionEffectContributor(Progression),
                });
                var attributes = new AttributeSystem(Ecs, effects, Options.Create(new DeathOptions { HpFloor = -10 }));
                var relativePower = new RelativePowerSystem(
                    Ecs, new StatSystem(attributes, effects), new PowerBudgetSystem(PowerBudgetTunables.Default));
                Bus = new RecordingEventBus(dispatch: false);
                Handler = new AdvancementHandler(Progression, relativePower, new AbilityRegistry(), Bus);
            }
        }

        private static uint CreateCombatant(EntityService ecs, int power = 10)
            => new EntityBuilder(ecs).AsPlayer().WithAttributes(power, power, power, power).Build();

        private static uint CreateMob(EntityService ecs, int power = 10)
            => new EntityBuilder(ecs).AsMob("rat").WithAttributes(power, power, power, power).Build();

        /// <summary>
        /// Effective power ~15 against a fresh 386 — ratio ≈ 0.04, far under the 0.25 floor.
        /// HpMax is weighted, so the pool is shrunk too (attributes alone would not make it trivial).
        /// </summary>
        private static uint CreateTrivialMob(EntityService ecs)
            => new EntityBuilder(ecs).AsMob("gnat").WithAttributes(1, 1, 1, 1).WithPools(hp: 1).Build();

        private static uint CreateTrivialPlayer(EntityService ecs)
            => new EntityBuilder(ecs).AsPlayer().WithAttributes(1, 1, 1, 1).WithPools(hp: 1).Build();

        // ── CombatKill (behaviour preserved from the pre-slice handler) ───────────

        [Fact]
        public async Task Peer_kill_publishes_one_ExperienceAwardedEvent_per_track()
        {
            var world = new TestWorld(new FakeRandom(new[] { 10, 10 }));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(ProgressionConstants.CombatTracks.Length, awarded.Count);
            Assert.All(awarded, e =>
            {
                Assert.Equal(killer, e.EntityId);
                Assert.Equal(10, e.Amount);
                Assert.Equal(XpSource.CombatKill, e.Source);
            });
        }

        [Fact]
        public async Task No_threshold_crossed_publishes_no_TrackImprovedEvent()
        {
            var world = new TestWorld(new FakeRandom(new[] { 10, 10 }));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            Assert.Empty(world.Bus.Published.OfType<TrackImprovedEvent>());
        }

        [Fact]
        public async Task Threshold_crossed_publishes_one_TrackImprovedEvent_per_crossing()
        {
            // The kill row's base range is 8-12 per track; repeated kills accumulate until the
            // first threshold (100) is crossed on the Body track — assert exactly one crossing.
            var world = new TestWorld(new FakeRandom(seed: 7));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            for (var i = 0; i < 20 && world.Progression.GetImprovementCount(killer, ScoreId.Body) == 0; i++)
                await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            Assert.Equal(1, world.Progression.GetImprovementCount(killer, ScoreId.Body));

            var improved = world.Bus.Published.OfType<TrackImprovedEvent>()
                .Where(e => e.Track == ProgressionTrack.Of(ScoreId.Body)).ToList();
            Assert.Single(improved);
            Assert.Equal(killer, improved[0].EntityId);
            Assert.Equal(1, improved[0].NewImprovementCount);
        }

        [Fact]
        public async Task KillerEntityId_zero_publishes_nothing()
        {
            // The discard is AdvancementEligibility data on the rule, not a branch in the handler —
            // the handler maps fields and calls the system unconditionally (INV-8).
            var world = new TestWorld(new FakeRandom(seed: 1)); // no draws should be consumed
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", KillerEntityId: 0));

            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Trivial_victim_kill_publishes_nothing()
        {
            // Ratio gathered on effective power (victim ÷ killer) is under the floor — ineligible,
            // and an empty FakeRandom proves zero draws (any draw would throw).
            var world = new TestWorld(new FakeRandom());
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateTrivialMob(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.gnat", killer));

            Assert.Empty(world.Bus.Published);
        }

        // ── AbilityUse ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Offensive_ability_use_against_a_trivial_target_publishes_nothing()
        {
            var world = new TestWorld(new FakeRandom()); // zero draws — any draw would throw
            var actor = CreateCombatant(world.Ecs);
            var target = CreateTrivialMob(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "kick", target, IsOffensive: true));

            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Offensive_ability_use_against_an_already_destroyed_target_is_ineligible()
        {
            // Defensive backstop: an opponent named but unreadable reads Ratio 0.0 — ineligible,
            // never a fallback to ratio-free XP (Post 11).
            var world = new TestWorld(new FakeRandom()); // zero draws — any draw would throw
            var actor = CreateCombatant(world.Ecs);
            var target = CreateMob(world.Ecs);
            world.Ecs.DestroyEntity(target);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "kick", target, IsOffensive: true));

            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Self_targeted_use_is_awarded_as_without_an_opponent()
        {
            // Targeting.Self publishes the actor's own id as the target (Verified 1) but is not
            // offensive — no opponent, no ratio, today's award.
            var rng = new FakeRandom(new[] { 4, 4 });
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);
            var world = new TestWorld(rng);
            var actor = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "empower", actor, IsOffensive: false));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(2, awarded.Count);
            Assert.All(awarded, e => Assert.Equal(4, e.Amount));
        }

        [Fact]
        public async Task Ally_targeted_non_offensive_use_applies_no_ratio_even_on_a_trivially_weak_target()
        {
            // A non-offensive use on ANOTHER entity names no opponent. Were the target's ratio
            // applied, this trivially weak target would make the use ineligible; instead the award
            // and draw sequence are exactly the no-opponent ones.
            var rng = new FakeRandom(new[] { 4, 4 });
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);
            var world = new TestWorld(rng);
            var actor = CreateCombatant(world.Ecs);
            var ally = CreateTrivialPlayer(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "kick", ally, IsOffensive: false));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(2, awarded.Count);
            Assert.All(awarded, e =>
            {
                Assert.Equal(actor, e.EntityId);
                Assert.Equal(4, e.Amount);
            });
        }

        [Fact]
        public async Task Ability_use_awards_the_ability_track_and_its_attribute_track()
        {
            // kick declares XpAttributeTrack = Body, so two candidates: ability:kick then Body.
            // Chance 0.25 → one NextDouble per candidate; a passing roll then draws the amount.
            var rng = new FakeRandom(new[] { 4, 4 });
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);

            var world = new TestWorld(rng);
            var actor = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "kick", TargetEntityId: null));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(2, awarded.Count);
            Assert.All(awarded, e =>
            {
                Assert.Equal(actor, e.EntityId);
                Assert.Equal(XpSource.AbilityUse, e.Source);
                Assert.Equal(4, e.Amount);
            });
            Assert.Contains(awarded, e => e.Track == ProgressionTrack.Ability("kick"));
            Assert.Contains(awarded, e => e.Track == ProgressionTrack.Of(ScoreId.Body));
        }

        [Fact]
        public async Task Ability_use_by_a_non_player_actor_awards_nothing()
        {
            var world = new TestWorld(new FakeRandom(seed: 1)); // no draws should be consumed
            var mob = CreateMob(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(mob, "kick", TargetEntityId: null));

            Assert.Empty(world.Bus.Published);
            Assert.Equal(0, world.Progression.GetXp(mob, ProgressionTrack.Ability("kick")));
        }

        [Fact]
        public async Task Ability_use_that_fails_the_chance_roll_publishes_nothing()
        {
            var rng = new FakeRandom(seed: 1);
            rng.EnqueueDouble(0.99);
            rng.EnqueueDouble(0.99);

            var world = new TestWorld(rng);
            var actor = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new AbilityActivatedEvent(actor, "kick", TargetEntityId: null));

            Assert.Empty(world.Bus.Published);
        }

        // ── DamageTaken ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Combat_round_awards_the_defender_and_not_the_attacker()
        {
            var rng = new FakeRandom(new[] { 3, 3 });
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);

            var world = new TestWorld(rng);
            var attacker = CreateCombatant(world.Ecs);
            var defender = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new CombatRoundEvent(
                attacker, defender, RoomEntityId: 1u,
                new CombatRoundResult(attacker, defender, DamageDealt: 7, AttackerHit: true, CombatRoundOutcome.Hit)));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(ProgressionConstants.CombatTracks.Length, awarded.Count);
            Assert.All(awarded, e =>
            {
                Assert.Equal(defender, e.EntityId);
                Assert.Equal(XpSource.DamageTaken, e.Source);
            });
            Assert.Equal(0, world.Progression.GetXp(attacker, ScoreId.Body));
        }

        [Fact]
        public async Task Combat_round_from_a_trivial_attacker_gives_the_defender_nothing()
        {
            // The attacker is the defender's opponent: attacker ÷ defender under the floor is
            // ineligible with zero draws (any draw on the empty FakeRandom would throw).
            var world = new TestWorld(new FakeRandom());
            var attacker = CreateTrivialMob(world.Ecs);
            var defender = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new CombatRoundEvent(
                attacker, defender, RoomEntityId: 1u,
                new CombatRoundResult(attacker, defender, DamageDealt: 7, AttackerHit: true, CombatRoundOutcome.Hit)));

            Assert.Empty(world.Bus.Published);
            Assert.Equal(0, world.Progression.GetXp(defender, ScoreId.Body));
        }

        [Fact]
        public async Task Zero_damage_round_awards_nothing()
        {
            var world = new TestWorld(new FakeRandom(seed: 1)); // no draws should be consumed
            var attacker = CreateCombatant(world.Ecs);
            var defender = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new CombatRoundEvent(
                attacker, defender, RoomEntityId: 1u,
                new CombatRoundResult(attacker, defender, DamageDealt: 0, AttackerHit: false, CombatRoundOutcome.Miss)));

            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task Ability_strike_awards_the_defender_damage_taken()
        {
            var rng = new FakeRandom(new[] { 3, 3 });
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);

            var world = new TestWorld(rng);
            var attacker = CreateCombatant(world.Ecs);
            var defender = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new AbilityStrikeResolvedEvent(
                attacker, defender, RoomEntityId: 1u,
                new CombatRoundResult(attacker, defender, DamageDealt: 5, AttackerHit: true, CombatRoundOutcome.Hit),
                AbilityId: "kick", DefenderName: "rat"));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.All(awarded, e => Assert.Equal(defender, e.EntityId));
            Assert.NotEmpty(awarded);
        }

        [Fact]
        public async Task Damage_taken_by_a_mob_awards_nothing()
        {
            // Without the player-earner gate every mob in every combat round would accrue XP.
            var world = new TestWorld(new FakeRandom(seed: 1)); // no draws should be consumed
            var attacker = CreateCombatant(world.Ecs);
            var mob = CreateMob(world.Ecs);

            await world.Handler.HandleAsync(new CombatRoundEvent(
                attacker, mob, RoomEntityId: 1u,
                new CombatRoundResult(attacker, mob, DamageDealt: 9, AttackerHit: true, CombatRoundOutcome.Hit)));

            Assert.Empty(world.Bus.Published);
            Assert.Empty(world.Progression.GetTrackedTracks(mob));
        }
    }
}
