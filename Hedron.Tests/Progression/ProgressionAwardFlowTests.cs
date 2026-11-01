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
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Components;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Commands;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Commands;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Simulation.Systems;
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
    /// kills a mob, and asserts the Main-Flow postconditions end-to-end. Extended by the
    /// relative-power slice (WP-2): the kill ratio on effective power (gear lowers it), the
    /// ability-use gather through the real <see cref="AbilityInvocationPipeline"/>, and the
    /// live/sandbox parity pin (SR11).
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
            public IRelativePowerSystem RelativePower { get; }
            public AbilityInvocationPipeline Pipeline { get; }
            public CombatSystem Combat { get; }
            public EntityStateService EntityState { get; }
            public RecordingEventBus Bus { get; }
            public RecordingBroadcastSystem Broadcast { get; private set; } = null!;
            public IPreferenceSystem Preferences { get; private set; } = null!;

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();

                Progression = new ProgressionSystem(Ecs, rng, new AdvancementRuleRegistry());
                var contributor = new ProgressionEffectContributor(Progression);
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { new EquipmentEffectContributor(Ecs), contributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(Ecs, effects, deathOpts);
                Stats = new StatSystem(attributes, effects);
                RelativePower = new RelativePowerSystem(Ecs, Stats, new PowerBudgetSystem(PowerBudgetTunables.Default));

                var aspects = new AspectSystem(Ecs);
                Combat = new CombatSystem(Ecs, Stats, attributes, aspects, rng);
                EntityState = new EntityStateService(Ecs);

                Bus = new RecordingEventBus(dispatch: true);

                var deathSystem = new NoOpDeathSystem();
                var tickHandler = new CombatTickHandler(Ecs, Combat, EntityState, deathSystem, Stats, Bus, NullLogger<CombatTickHandler>.Instance);
                var mobDeathHandler = new CombatMobDeathHandler(Ecs, EntityState, Bus);
                Broadcast = new RecordingBroadcastSystem();
                Preferences = new PreferenceSystem(Ecs);
                var advancement = new AdvancementHandler(Progression, RelativePower, new AbilityRegistry(), Bus);
                var narration = new ProgressionNarrationHandler(Preferences, Broadcast);

                var abilities = new AbilitySystem(
                    Ecs, new AbilityRegistry(), effects, new Hedron.Core.Modules.Effects.EffectRegistry(), attributes, EntityState);
                Pipeline = new AbilityInvocationPipeline(
                    abilities, Combat, EntityState, Ecs, Bus, NullLogger<AbilityInvocationPipeline>.Instance);
                var strikeHandler = new AbilityStrikeHandler(Ecs, Broadcast, new AbilityRegistry(), Bus);

                Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(tickHandler);
                Bus.Subscribe<CombatEndedEvent>(mobDeathHandler);
                Bus.Subscribe<Hedron.Core.Modules.Mobs.Events.MobDiedEvent>(advancement);
                Bus.Subscribe<CombatRoundEvent>(advancement);
                Bus.Subscribe<Hedron.Core.Modules.Abilities.Events.AbilityActivatedEvent>(advancement);
                Bus.Subscribe<AbilityStrikeResolvedEvent>(strikeHandler);
                Bus.Subscribe<AbilityStrikeResolvedEvent>(advancement);
                Bus.Subscribe<Hedron.Core.Modules.Progression.Events.ExperienceAwardedEvent>(narration);
                Bus.Subscribe<Hedron.Core.Modules.Progression.Events.TrackImprovedEvent>(narration);
            }
        }

        /// <summary>
        /// A peer of the 100-HP fresh player on effective power (MaxHp 100), wounded to 1 HP so a
        /// single 1-damage blow kills it. Effective power ignores current HP (SR3), so the kill
        /// ratio is exactly 1.0. (Pre-slice these fixtures used a 1-max-HP mob, which the raw
        /// attribute proxy could not see; on effective power that mob is a ratio-0.49 kill.)
        /// </summary>
        private static uint MakeWoundedPeerRat(EntityService ecs, uint roomId, int body = 10)
        {
            var id = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: body)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .Build();
            ecs.Get<PoolsComponent>(id).CurrentHp = 1;
            return id;
        }

        [Fact]
        public async Task MobDeath_awards_combat_experience_and_folds_into_effective_score()
        {
            // combat: hit=20, damage=1 (kills the 1-HP wounded mob); progression: Body base=12,
            // HpMax base=12 (killer/victim both effective power 386 → peer scale 1.0, full base each).
            var rng = new FakeRandom(20, 1, 12, 12);
            var world = new TestWorld(rng);

            const uint roomId = 1u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = MakeWoundedPeerRat(world.Ecs, roomId);

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

            uint MakeMob() => MakeWoundedPeerRat(world.Ecs, roomId);

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

        // ── Anti-grind on effective power (relative-power WP-2) ──────────────────

        /// <summary>
        /// SR8 headline: the <b>same</b> kill awards less XP once the killer wears gear — the
        /// ratio is victim ÷ killer on effective power, so the killer's gear lowers it (Post 8).
        /// </summary>
        [Fact]
        public async Task The_same_kill_awards_less_once_the_killer_wears_gear()
        {
            // Two kills, 4 ints each: combat hit+damage, then progression Body+HpMax base (12, 12).
            var rng = new FakeRandom(20, 1, 12, 12, 20, 1, 12, 12);
            var world = new TestWorld(rng);

            const uint roomId = 5u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId).Build();

            async Task<int> KillOneAsync(uint tick)
            {
                var mobId = MakeWoundedPeerRat(world.Ecs, roomId);
                var before = world.Progression.GetXp(playerId, ScoreId.Body);
                world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
                world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
                world.Combat.StartCombat(playerId, mobId);
                await world.Bus.PublishAsync(Ticks.At(tick));
                Assert.False(world.Ecs.HasComponent<MobDataComponent>(mobId), "the mob must have died");
                return world.Progression.GetXp(playerId, ScoreId.Body) - before;
            }

            var unarmed = await KillOneAsync(1);

            var weapon = world.Ecs.CreateEntity().Id;
            world.Ecs.AddComponent(weapon, new ItemDataComponent
            {
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 25) },
            });
            world.Ecs.AddComponent(playerId, new EquipmentComponent { Slots = { [Hedron.Core.WornSlot.MainHand] = weapon } });

            // The ratio the handler will gather for an identical (fresh, peer) rat, read up front.
            var probe = MakeWoundedPeerRat(world.Ecs, 999u);
            var armedRatio = world.RelativePower.Relate(playerId, probe).Ratio;
            world.Ecs.DestroyEntity(probe);

            var armed = await KillOneAsync(2);

            Assert.Equal(12, unarmed);
            Assert.True(armedRatio < 1.0, $"gear must lower the kill ratio (got {armedRatio})");
            Assert.Equal((int)System.Math.Round(12 * armedRatio, System.MidpointRounding.AwayFromZero), armed);
            Assert.True(armed < unarmed, $"armed kill awarded {armed}, unarmed {unarmed}");
        }

        /// <summary>
        /// Verified 2: <see cref="AbilityInvocationPipeline"/> publishes <c>AbilityActivatedEvent</c>
        /// before the strike resolves, so a killing-blow <c>kick</c> has its ratio gathered while the
        /// target is still live — the use is scaled by the target's ratio, never treated as
        /// unreadable (which would award nothing).
        /// </summary>
        [Fact]
        public async Task A_killing_blow_kick_applies_the_targets_ratio_gathered_before_the_strike()
        {
            // Ability use (2 candidates: ability:kick, Body): chance roll + amount 6 each; strike
            // damage roll 1; then the kill award: Body + HpMax base 10 each.
            var rng = new FakeRandom(6, 6, 1, 10, 10);
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);
            var world = new TestWorld(rng);

            const uint roomId = 6u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).InRoom(roomId)
                .With(new AbilitiesComponent { Known = { "kick" } })
                .Build();
            // A weaker (Body 5) wounded rat: ratio < 1 so a scaled award is distinguishable from
            // both the unscaled 6 and the unreadable 0.
            var mobId = MakeWoundedPeerRat(world.Ecs, roomId, body: 5);
            var ratio = world.RelativePower.Relate(playerId, mobId).Ratio;
            Assert.InRange(ratio, ProgressionConstants.AntiGrindFloorRatio, 0.99);

            var output = new RecordingOutput();
            await world.Pipeline.InvokeAsync(
                playerId, "kick", new AbilityRegistry().Get("kick"), "rat", output.WriterFor(playerId), "test");

            Assert.False(world.Ecs.HasComponent<MobDataComponent>(mobId), "the kick must have killed and destroyed the mob");
            var activated = Assert.Single(world.Bus.Published.OfType<Hedron.Core.Modules.Abilities.Events.AbilityActivatedEvent>());
            Assert.True(activated.IsOffensive);
            Assert.Equal(mobId, activated.TargetEntityId);

            var expected = (int)System.Math.Round(6 * ratio, System.MidpointRounding.AwayFromZero);
            Assert.Equal(expected, world.Progression.GetXp(playerId, ProgressionTrack.Ability("kick")));
            Assert.True(expected is > 0 and < 6);
        }

        /// <summary>
        /// Verified 1: a self-cast <c>empower</c> publishes a <b>non-null</b> target (the actor) with
        /// <c>IsOffensive == false</c> — so the handler names no opponent and the use is awarded on
        /// today's draws.
        /// </summary>
        [Fact]
        public async Task A_self_cast_empower_publishes_IsOffensive_false_with_the_actor_as_target()
        {
            var rng = new FakeRandom(5, 5);
            rng.EnqueueDouble(0.0);
            rng.EnqueueDouble(0.0);
            var world = new TestWorld(rng);

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer().WithAttributes().WithPools(hp: 100).InRoom(7u)
                .With(new AbilitiesComponent { Known = { "empower" } })
                .Build();

            var output = new RecordingOutput();
            await world.Pipeline.InvokeAsync(
                playerId, "empower", new AbilityRegistry().Get("empower"), null, output.WriterFor(playerId), "test");

            var activated = Assert.Single(world.Bus.Published.OfType<Hedron.Core.Modules.Abilities.Events.AbilityActivatedEvent>());
            Assert.False(activated.IsOffensive);
            Assert.Equal(playerId, activated.TargetEntityId);

            Assert.Equal(5, world.Progression.GetXp(playerId, ProgressionTrack.Ability("empower")));
            Assert.Equal(5, world.Progression.GetXp(playerId, ScoreId.Mind));
        }

        /// <summary>
        /// SR11 / Post 12 — live/sandbox parity: the same killer/victim pair, composed in a live
        /// world (production contributor set; a mob's tier on <c>MobDataComponent.Tier</c>) and in
        /// <see cref="SandboxWorldFactory.Create"/> (a combatant's tier on <c>AscensionComponent</c>,
        /// as <c>SimCombatantFactory</c> materializes it), gathers the same ratio and produces the
        /// same award rows kill-for-kill at the same seed — including after the killer's own
        /// improvements move the ratio.
        /// <para>
        /// Tier 0 only since combat-effective-scores: at Tier 0 neither side carries a tier fold, so the
        /// two compositions agree exactly. Above Tier 0 they deliberately diverge — see
        /// <see cref="Above_tier_0_a_sandbox_combatant_reads_stronger_than_a_live_mob_of_the_same_tier"/>.
        /// </para>
        /// </summary>
        [Fact]
        public void A_live_kill_and_a_sandbox_kill_of_the_same_pair_gather_the_same_ratio_and_rows()
        {
            var (liveRatios, simRatios, liveRowsAll, simRowsAll, improved) = RunLiveAndSandboxKills(victimTier: 0);

            Assert.Equal(liveRatios, simRatios);
            Assert.Equal(liveRowsAll, simRowsAll);
            Assert.True(improved, "the run must reach an improvement so the parity covers a moved ratio");
        }

        /// <summary>
        /// Known tier gap (combat-effective-scores; tracked with Phase 6 tier tuning in
        /// <c>docs/roadmap/backlog.md</c>). A live mob's tier lives on <c>MobDataComponent.Tier</c> and only
        /// feeds the oracle, which projects the tier baseline onto Body/HpMax. A sandbox combatant's tier
        /// rides <c>AscensionComponent</c>, folds onto Body/HpMax for real, and — since combat reads
        /// effective Body — also raises AttackPower/Defense, which the oracle's projection does not. So a
        /// sandbox tier-1 victim reads (and fights) stronger than a live tier-1 mob with the same stats.
        /// Pinned so the gap is visible; closing it is Phase 6 (mob tier folding + the oracle projection).
        /// </summary>
        [Fact]
        public void Above_tier_0_a_sandbox_combatant_reads_stronger_than_a_live_mob_of_the_same_tier()
        {
            var (liveRatios, simRatios, _, _, _) = RunLiveAndSandboxKills(victimTier: 1);

            Assert.Equal(1.310880829015544, liveRatios[0], precision: 12);
            Assert.Equal(1.4766839378238341, simRatios[0], precision: 12);
        }

        private static (List<double> LiveRatios, List<double> SimRatios, List<string> LiveRows, List<string> SimRows, bool Improved)
            RunLiveAndSandboxKills(int victimTier)
        {
            const int seed = 2026;
            const int kills = 15; // crosses the first threshold on both tracks, so the ratio moves

            // ── Live composition ──
            var liveEcs = new EntityService();
            var liveProgression = new ProgressionSystem(liveEcs, new FakeRandom(seed), new AdvancementRuleRegistry());
            var abilityRegistry = new AbilityRegistry();
            var effectRegistry = new Hedron.Core.Modules.Effects.EffectRegistry();
            var liveEffects = new EffectSystem(liveEcs, new IEffectContributor[]
            {
                new EquipmentEffectContributor(liveEcs),
                new AbilityEffectContributor(liveEcs, abilityRegistry, effectRegistry),
                new ProgressionEffectContributor(liveProgression),
                new AscensionEffectContributor(new AscensionSystem(liveEcs)),
            });
            var liveAttributes = new AttributeSystem(liveEcs, liveEffects, Options.Create(new DeathOptions { HpFloor = -10 }));
            var liveRelative = new RelativePowerSystem(
                liveEcs, new StatSystem(liveAttributes, liveEffects), new PowerBudgetSystem(PowerBudgetTunables.Default));

            var liveKiller = new EntityBuilder(liveEcs).AsPlayer().WithAttributes().WithPools(hp: 100).Build();
            var liveVictim = new EntityBuilder(liveEcs).AsMob("wolf").WithAttributes().WithPools(hp: 100).Build();
            liveEcs.Get<MobDataComponent>(liveVictim).Tier = victimTier;

            // ── Sandbox composition ──
            var sandbox = new SandboxWorldFactory(
                    abilityRegistry, effectRegistry, new PowerBudgetSystem(PowerBudgetTunables.Default),
                    Options.Create(new DeathOptions()))
                .Create(new FakeRandom(seed));

            uint Materialize(int tier)
            {
                // Mirrors SimCombatantFactory.Materialize: mob-shaped, tier on AscensionComponent.
                var id = sandbox.EntityService.CreateEntity().Id;
                sandbox.EntityService.AddComponent(id, new MobDataComponent { Name = "combatant" });
                sandbox.EntityService.AddComponent(id, new AttributesComponent { Level = 1, Mind = 10, Body = 10, Spirit = 10, Attunement = 10 });
                sandbox.EntityService.AddComponent(id, new PoolsComponent { MaxHp = 100, CurrentHp = 100 });
                if (tier > 0)
                    sandbox.EntityService.AddComponent(id, new AscensionComponent { Tier = tier });
                return id;
            }

            var simKiller = Materialize(0);
            var simVictim = Materialize(victimTier);

            var liveRatios = new List<double>();
            var simRatios = new List<double>();
            var liveRowsAll = new List<string>();
            var simRowsAll = new List<string>();
            for (var kill = 1; kill <= kills; kill++)
            {
                var liveRatio = liveRelative.Relate(liveKiller, liveVictim).Ratio;
                var simRatio = sandbox.RelativePower.Relate(simKiller, simVictim).Ratio;
                liveRatios.Add(liveRatio);
                simRatios.Add(simRatio);

                // Each arm awards from its own ratio, so the rows track what each composition would pay.
                var liveRows = liveProgression.AwardCombatExperience(liveKiller, liveVictim, liveRatio).Tracks;
                var simRows = sandbox.Progression.AwardCombatExperience(simKiller, simVictim, simRatio).Tracks;
                liveRowsAll.Add(string.Join(";", liveRows));
                simRowsAll.Add(string.Join(";", simRows));
            }

            return (liveRatios, simRatios, liveRowsAll, simRowsAll,
                liveProgression.GetImprovementCount(liveKiller, ScoreId.Body) >= 1);
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
            var mobId = MakeWoundedPeerRat(world.Ecs, roomId);

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
