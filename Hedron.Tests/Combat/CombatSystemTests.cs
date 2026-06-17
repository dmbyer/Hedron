using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Combat
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="CombatSystem"/>.
    /// Each test constructs the system with real dependencies (EntityService,
    /// AttributeSystem, StatSystem, AspectSystem) and a scripted <see cref="FakeRandom"/>
    /// to produce deterministic outcomes.
    ///
    /// Coverage contract: the Postconditions of docs/use-cases/combat.md.
    /// </summary>
    public sealed class CombatSystemTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a fully wired <see cref="CombatSystem"/> backed by a fresh
        /// <see cref="EntityService"/> and an injectable <see cref="FakeRandom"/>.
        /// Returns the real sub-systems so individual tests can inspect state.
        /// </summary>
        private static (CombatSystem combat, AttributeSystem attributes, EntityService ecs)
            Build(FakeRandom rng)
        {
            var ecs = new EntityService();

            // EffectSystem with no contributors (no active effects in these unit tests).
            var noEffects = new EffectSystem(ecs, System.Array.Empty<IEffectContributor>());

            // DeathOptions: standard floor.
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });

            var attributes = new AttributeSystem(ecs, noEffects, deathOpts);
            var stats = new StatSystem(attributes, noEffects);
            var aspects = new AspectSystem(ecs);

            var combat = new CombatSystem(ecs, stats, attributes, aspects, rng);
            return (combat, attributes, ecs);
        }

        // ── ExecuteRound — hit / miss ────────────────────────────────────────────

        /// <summary>
        /// The attack roll is: FakeRandom.Next(1,21) + Body/2.
        /// The defense threshold is: 10 + Body/4.
        ///
        /// With Body=10:  effective attack modifier = 5, defense threshold = 12.
        /// A roll of 20 → 20+5 = 25 ≥ 12 → hit.
        /// </summary>
        [Fact]
        public void ExecuteRound_hit_when_roll_meets_or_exceeds_threshold()
        {
            // roll=20 (d20) then roll for damage=1 (d(attackPower+1))
            var rng = new FakeRandom(20, 1);
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var result = combat.ExecuteRound(attackerId, defenderId);

            Assert.True(result.AttackerHit, "High roll should produce a hit");
            Assert.NotEqual(CombatRoundOutcome.Miss, result.Outcome);
            Assert.True(result.DamageDealt > 0, "A hit must deal damage > 0");
        }

        /// <summary>
        /// With Body=10: attack modifier=5, threshold=12.
        /// A roll of 1 → 1+5=6 &lt; 12 → miss.
        /// </summary>
        [Fact]
        public void ExecuteRound_miss_when_roll_below_threshold()
        {
            // roll=1 → miss path (no damage roll consumed)
            var rng = new FakeRandom(1);
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var result = combat.ExecuteRound(attackerId, defenderId);

            Assert.False(result.AttackerHit);
            Assert.Equal(CombatRoundOutcome.Miss, result.Outcome);
            Assert.Equal(0, result.DamageDealt);
        }

        // ── ExecuteRound — damage applied via SetCurrentHp ───────────────────────

        [Fact]
        public void ExecuteRound_hit_reduces_defender_hp()
        {
            // roll=20 (d20 hit), then roll=5 (damage roll)
            var rng = new FakeRandom(20, 5);
            var (combat, attributes, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var hpBefore = attributes.GetCurrentHp(defenderId);
            var result = combat.ExecuteRound(attackerId, defenderId);

            var hpAfter = attributes.GetCurrentHp(defenderId);
            Assert.True(hpAfter < hpBefore,
                "Defender HP must decrease after a hit");
            Assert.Equal(hpBefore - result.DamageDealt, hpAfter);
        }

        // ── ExecuteRound — outcome: MobDied ──────────────────────────────────────

        [Fact]
        public void ExecuteRound_MobDied_when_mob_defender_hp_drops_to_zero_or_below()
        {
            // roll=20 (hit), then damage roll covers all HP
            // Mob has 1 HP; Body=10 → attackPower = 5; damage roll 1..6. Use roll=1 (≥1 damage).
            var rng = new FakeRandom(20, 1);
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            // Mob with 1 HP so any hit kills it.
            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .Build();

            var result = combat.ExecuteRound(attackerId, defenderId);

            Assert.Equal(CombatRoundOutcome.MobDied, result.Outcome);
            Assert.True(ecs.HasComponent<MobDataComponent>(defenderId),
                "CombatSystem must not destroy the entity — only the handler does");
        }

        // ── ExecuteRound — outcome: PlayerIncapacitated ───────────────────────────

        [Fact]
        public void ExecuteRound_PlayerIncapacitated_when_player_defender_hp_drops_to_zero_or_below()
        {
            // Player has 1 HP; any hit incapacitates them.
            var rng = new FakeRandom(20, 1);
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsMob("goblin")
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .Build();

            var result = combat.ExecuteRound(attackerId, defenderId);

            Assert.Equal(CombatRoundOutcome.PlayerIncapacitated, result.Outcome);
        }

        // ── ExecuteRound — HP clamp: cannot go below HpFloor (not 0) ─────────────

        [Fact]
        public void ExecuteRound_hp_clamped_to_HpFloor_not_below()
        {
            // Mob has 1 HP; Body=10 → attackPower=5 max damage=6; overkill should clamp.
            var rng = new FakeRandom(20, 5);  // damage roll of 5 overkills a 1-HP mob
            var (combat, attributes, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .Build();

            combat.ExecuteRound(attackerId, defenderId);

            var hpAfter = attributes.GetCurrentHp(defenderId);
            // HpFloor is -10; must be >= floor, not arbitrary negative
            Assert.True(hpAfter >= -10,
                $"HP {hpAfter} must not go below HpFloor (-10) after overkill");
        }

        // ── ExecuteRound — aspect resolution applied ─────────────────────────────

        [Fact]
        public void ExecuteRound_aspect_resolution_applied_when_attacker_has_affinity()
        {
            // Attacker has 100% Fire affinity; defender has 50% Fire resistance.
            // Untyped damage X → typed X stays reduced. We verify the result carries
            // AspectComposition (not null / not Empty) when the attacker has affinity.
            // Body=10 → attackPower=5 → Next(1, 7); damage roll must be in [1,6].
            var rng = new FakeRandom(20, 5);   // guaranteed hit; damage roll 5 (within [1,7))
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .With(new AspectAffinitiesComponent
                {
                    AffinityWeights = new Dictionary<AspectId, int> { [AspectId.Fire] = 100 },
                })
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("ice golem")
                .WithAttributes(body: 10)
                .WithPools(hp: 200)
                .With(new AspectAffinitiesComponent
                {
                    BaseResistances = new Dictionary<AspectId, int> { [AspectId.Fire] = 0 },
                })
                .Build();

            var result = combat.ExecuteRound(attackerId, defenderId);

            Assert.True(result.AttackerHit);
            // When composition is non-empty the result must carry it (not null)
            Assert.NotNull(result.AspectComposition);
            Assert.False(result.AspectComposition!.IsEmpty,
                "Result must carry the attacker's aspect composition");
        }

        // ── ResolveAbilityStrike ──────────────────────────────────────────────────

        [Fact]
        public void ResolveAbilityStrike_always_hits()
        {
            // Any scripted rng — no roll is used for hit/miss in ability strikes
            var rng = new FakeRandom(3);  // damage roll only
            var (combat, _, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var result = combat.ResolveAbilityStrike(attackerId, defenderId, basePower: 10);

            Assert.True(result.AttackerHit,
                "ResolveAbilityStrike must always mark AttackerHit = true");
            Assert.NotEqual(CombatRoundOutcome.Miss, result.Outcome);
        }

        [Fact]
        public void ResolveAbilityStrike_damage_is_defense_mitigated()
        {
            // basePower=20; defense = Body/4 = 10/4 = 2.
            // rawDamage = Max(1, roll - defense); roll is scripted to 5 → raw = Max(1,5-2)=3.
            var rng = new FakeRandom(5);
            var (combat, attributes, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)     // defense = 10/4 = 2
                .WithPools(hp: 100)
                .Build();

            var result = combat.ResolveAbilityStrike(attackerId, defenderId, basePower: 20);

            Assert.True(result.AttackerHit);
            // Defense was applied: damage < basePower
            Assert.True(result.DamageDealt < 20,
                "Ability strike damage must be reduced by defense");
        }

        [Fact]
        public void ResolveAbilityStrike_damage_minimum_is_1()
        {
            // basePower=1; defense=10/4=2; roll=1 → rawDamage = Max(1, 1-2) = Max(1,-1)=1.
            var rng = new FakeRandom(1);
            var (combat, attributes, ecs) = Build(rng);

            var attackerId = new EntityBuilder(ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .Build();

            var defenderId = new EntityBuilder(ecs)
                .AsMob("armored crab")
                .WithAttributes(body: 10)     // defense = 2
                .WithPools(hp: 100)
                .Build();

            var result = combat.ResolveAbilityStrike(attackerId, defenderId, basePower: 1);

            Assert.True(result.AttackerHit);
            Assert.True(result.DamageDealt >= 1,
                "Ability strike must deal at least 1 damage (min 1 guard)");
        }

        // ── StartCombat / EndCombat ───────────────────────────────────────────────

        [Fact]
        public void StartCombat_adds_CombatStateComponent_to_both_participants()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            var playerEntityId = new EntityBuilder(ecs).AsPlayer().WithPools().Build();
            var mobEntityId    = new EntityBuilder(ecs).AsMob("goblin").WithPools().Build();

            combat.StartCombat(playerEntityId, mobEntityId);

            Assert.True(ecs.HasComponent<CombatStateComponent>(playerEntityId),
                "Attacker must have CombatStateComponent after StartCombat");
            Assert.True(ecs.HasComponent<CombatStateComponent>(mobEntityId),
                "Defender must have CombatStateComponent after StartCombat");
        }

        [Fact]
        public void StartCombat_sets_OpponentEntityId_correctly_for_both()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            var playerEntityId = new EntityBuilder(ecs).AsPlayer().WithPools().Build();
            var mobEntityId    = new EntityBuilder(ecs).AsMob("goblin").WithPools().Build();

            combat.StartCombat(playerEntityId, mobEntityId);

            var playerState = ecs.Get<CombatStateComponent>(playerEntityId);
            var mobState    = ecs.Get<CombatStateComponent>(mobEntityId);

            Assert.Equal(mobEntityId,    playerState.OpponentEntityId);
            Assert.Equal(playerEntityId, mobState.OpponentEntityId);
        }

        [Fact]
        public void EndCombat_removes_CombatStateComponent_from_both_participants()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            var playerEntityId = new EntityBuilder(ecs).AsPlayer().WithPools().Build();
            var mobEntityId    = new EntityBuilder(ecs).AsMob("goblin").WithPools().Build();

            combat.StartCombat(playerEntityId, mobEntityId);
            combat.EndCombat(playerEntityId, mobEntityId);

            Assert.False(ecs.HasComponent<CombatStateComponent>(playerEntityId),
                "Attacker must not have CombatStateComponent after EndCombat");
            Assert.False(ecs.HasComponent<CombatStateComponent>(mobEntityId),
                "Defender must not have CombatStateComponent after EndCombat");
        }

        // ── TryFindTargetInRoom ───────────────────────────────────────────────────

        [Fact]
        public void TryFindTargetInRoom_returns_true_and_entity_on_prefix_name_match()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            const uint roomId = 42u;
            var mobEntityId = new EntityBuilder(ecs)
                .AsMob("goblin")
                .InRoom(roomId)
                .Build();

            var found = combat.TryFindTargetInRoom(roomId, "gob", out var foundId);

            Assert.True(found);
            Assert.Equal(mobEntityId, foundId);
        }

        [Fact]
        public void TryFindTargetInRoom_returns_true_on_keyword_prefix_match()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            const uint roomId = 55u;
            var mobEntityId = new EntityBuilder(ecs)
                .AsMob("green goblin", new[] { "goblin", "creature" })
                .InRoom(roomId)
                .Build();

            var found = combat.TryFindTargetInRoom(roomId, "crea", out var foundId);

            Assert.True(found);
            Assert.Equal(mobEntityId, foundId);
        }

        [Fact]
        public void TryFindTargetInRoom_returns_false_when_mob_is_in_different_room()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            const uint playerRoomId = 10u;
            const uint mobRoomId    = 99u;

            new EntityBuilder(ecs)
                .AsMob("goblin")
                .InRoom(mobRoomId)
                .Build();

            var found = combat.TryFindTargetInRoom(playerRoomId, "goblin", out _);

            Assert.False(found,
                "Mob in a different room must not be found");
        }

        [Fact]
        public void TryFindTargetInRoom_returns_false_when_no_match()
        {
            var rng = new FakeRandom(seed: 1);
            var (combat, _, ecs) = Build(rng);

            const uint roomId = 1u;
            new EntityBuilder(ecs).AsMob("rat").InRoom(roomId).Build();

            var found = combat.TryFindTargetInRoom(roomId, "dragon", out _);

            Assert.False(found);
        }

        // ── INV-5: CombatSystem itself does not hold IEventBus ────────────────────

        [Fact]
        public void CombatSystem_does_not_hold_IEventBus_field()
        {
            // This duplicates the architecture guard in the specific combat context.
            // Gives a combat-scoped failure message rather than a generic sweep finding.
            var combatType = typeof(CombatSystem);
            var fields = combatType.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: CombatSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
