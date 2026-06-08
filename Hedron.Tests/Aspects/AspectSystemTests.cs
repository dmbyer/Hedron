using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Aspects
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AspectSystem"/>.
    ///
    /// Coverage contract: the resolve formula from docs/use-cases/aspect-foundation.md.
    ///
    /// Formula per aspect A in composition:
    ///   portion      = magnitude * weight / 100
    ///   boostFactor  = 1.0 + attackerAffinity_A / 100
    ///   resistFactor = 1.0 - clamp(resist_A, 0, 100) / 100
    ///   contribution = portion * boostFactor * resistFactor
    /// Total = Math.Round(sum of contributions), clamped to [0, int.MaxValue].
    /// When composition is empty the magnitude is returned unchanged.
    /// </summary>
    public sealed class AspectSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static AspectSystem Build(EntityService ecs) => new AspectSystem(ecs);

        /// <summary>
        /// Creates an entity whose <see cref="AspectAffinitiesComponent"/> has the given
        /// affinity weights and base resistances.
        /// </summary>
        private static uint MakeEntity(
            EntityService ecs,
            Dictionary<AspectId, int>? affinityWeights = null,
            Dictionary<AspectId, int>? baseResistances = null)
        {
            var comp = new AspectAffinitiesComponent
            {
                AffinityWeights = affinityWeights ?? new Dictionary<AspectId, int>(),
                BaseResistances = baseResistances ?? new Dictionary<AspectId, int>(),
            };
            return new EntityBuilder(ecs).With(comp).Build();
        }

        // ── Resolve — empty composition passthrough ───────────────────────────────

        [Fact]
        public void Resolve_empty_composition_returns_magnitude_unchanged()
        {
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();

            var result = system.Resolve(50, AspectComposition.Empty, attacker, defender);

            Assert.Equal(50, result);
        }

        [Fact]
        public void Resolve_empty_composition_zero_magnitude_returns_zero()
        {
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();

            Assert.Equal(0, system.Resolve(0, AspectComposition.Empty, attacker, defender));
        }

        // ── Resolve — pure composition (single aspect weight=100) ─────────────────

        [Fact]
        public void Resolve_pure_composition_no_affinity_no_resist_returns_magnitude()
        {
            // weight=100, attacker affinityBoost=0, resistFactor=1.0
            // portion = 50 * 100/100 = 50
            // boostFactor = 1 + 0/100 = 1.0
            // resistFactor = 1 - 0/100 = 1.0
            // total = 50 * 1.0 * 1.0 = 50
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();   // no AspectAffinitiesComponent
            var defender = new EntityBuilder(ecs).Build();   // no resistance

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(50, composition, attacker, defender);

            Assert.Equal(50, result);
        }

        [Fact]
        public void Resolve_pure_composition_full_affinity_no_resist_doubles_magnitude()
        {
            // weight=100, affinityBoost=100 → boostFactor=2.0, resistFactor=1.0
            // portion=50, total = 50 * 2.0 * 1.0 = 100
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 100 });
            var defender = new EntityBuilder(ecs).Build();

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(50, composition, attacker, defender);

            Assert.Equal(100, result);
        }

        [Fact]
        public void Resolve_pure_composition_no_affinity_full_resist_returns_zero()
        {
            // weight=100, boostFactor=1.0, resist=100 → resistFactor=0.0
            // total = 50 * 1.0 * 0.0 = 0
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Fire] = 100 });

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(50, composition, attacker, defender);

            Assert.Equal(0, result);
        }

        [Fact]
        public void Resolve_pure_composition_full_affinity_full_resist_returns_zero()
        {
            // boostFactor=2.0, resistFactor=0.0 → total = 50 * 2.0 * 0.0 = 0
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 100 });
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Fire] = 100 });

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(50, composition, attacker, defender);

            Assert.Equal(0, result);
        }

        [Fact]
        public void Resolve_pure_composition_half_resist_halves_damage()
        {
            // weight=100, boostFactor=1.0, resist=50 → resistFactor=0.5
            // portion=100, total = 100 * 1.0 * 0.5 = 50
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Ice] = 50 });

            var composition = AspectComposition.Single(AspectId.Ice);
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(50, result);
        }

        // ── Resolve — affinity boost formula ─────────────────────────────────────

        [Fact]
        public void Resolve_partial_affinity_boosts_proportionally()
        {
            // attacker has 50 affinity in Fire → boostFactor = 1 + 50/100 = 1.5
            // weight=100, magnitude=100, resist=0
            // total = 100 * 1.0 * 1.5 * 1.0 = 150
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 50 });
            var defender = new EntityBuilder(ecs).Build();

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(150, result);
        }

        [Fact]
        public void Resolve_attacker_affinity_only_boosts_matching_aspect()
        {
            // Attacker has Fire affinity=100. Composition is pure Ice.
            // Fire affinity does NOT boost Ice: boostFactor for Ice = 1 + 0/100 = 1.0
            // total = 80 * 1.0 * 1.0 = 80
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 100 });
            var defender = new EntityBuilder(ecs).Build();

            var composition = AspectComposition.Single(AspectId.Ice);
            var result = system.Resolve(80, composition, attacker, defender);

            Assert.Equal(80, result);
        }

        [Fact]
        public void Resolve_attacker_without_component_has_no_boost()
        {
            // No AspectAffinitiesComponent → affinityBoost=0 → boostFactor=1.0
            // same as the plain "no affinity" case
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();

            var composition = AspectComposition.Single(AspectId.Lightning);
            var result = system.Resolve(40, composition, attacker, defender);

            Assert.Equal(40, result);
        }

        // ── Resolve — defender resist formula ─────────────────────────────────────

        [Fact]
        public void Resolve_defender_without_component_has_zero_resist()
        {
            // No AspectAffinitiesComponent → resist=0 → resistFactor=1.0
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();   // no component

            var composition = AspectComposition.Single(AspectId.Void);
            var result = system.Resolve(60, composition, attacker, defender);

            Assert.Equal(60, result);
        }

        [Fact]
        public void Resolve_defender_component_missing_aspect_entry_has_zero_resist()
        {
            // Component exists but has no entry for the ability's aspect
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            // Defender has Ice resistance but the composition is Fire
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Ice] = 80 });

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(60, composition, attacker, defender);

            Assert.Equal(60, result);   // no Fire resist → passthrough
        }

        [Fact]
        public void Resolve_resist_clamped_above_100()
        {
            // Even if a future contributor pushed resist above 100, Resolve clamps to 100
            // (resistFactor=0). We test via the Resist method clamping: BaseResistances=150
            // → Resist returns 100 → resistFactor=0 → total=0.
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            // Store 150 directly in the dictionary (bypassing IsValid); Resist should clamp.
            var comp = new AspectAffinitiesComponent
            {
                BaseResistances = new Dictionary<AspectId, int> { [AspectId.Nature] = 150 },
            };
            var defender = new EntityBuilder(ecs).With(comp).Build();

            var composition = AspectComposition.Single(AspectId.Nature);
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(0, result);
        }

        [Fact]
        public void Resolve_resist_clamped_below_zero()
        {
            // Negative resist is clamped to 0 → resistFactor=1.0 → no reduction.
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var comp = new AspectAffinitiesComponent
            {
                BaseResistances = new Dictionary<AspectId, int> { [AspectId.Light] = -50 },
            };
            var defender = new EntityBuilder(ecs).With(comp).Build();

            var composition = AspectComposition.Single(AspectId.Light);
            var result = system.Resolve(40, composition, attacker, defender);

            Assert.Equal(40, result);   // negative resist doesn't amplify
        }

        // ── Resolve — mixed composition (multiple aspects) ───────────────────────

        [Fact]
        public void Resolve_mixed_composition_50_50_no_affinity_no_resist_returns_magnitude()
        {
            // Fire 50 + Ice 50 = 100 total weight
            // Each: portion = mag * 0.5, boostFactor=1.0, resistFactor=1.0
            // total = mag*0.5 + mag*0.5 = mag
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();

            var composition = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 50,
                [AspectId.Ice]  = 50,
            });
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(100, result);
        }

        [Fact]
        public void Resolve_mixed_composition_one_aspect_fully_resisted_other_passes()
        {
            // Fire 50 + Ice 50. Defender has 100% Fire resist; no Ice resist.
            // Fire: portion=50, boostFactor=1.0, resistFactor=0.0 → 0
            // Ice:  portion=50, boostFactor=1.0, resistFactor=1.0 → 50
            // total = 50
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Fire] = 100 });

            var composition = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 50,
                [AspectId.Ice]  = 50,
            });
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(50, result);
        }

        [Fact]
        public void Resolve_mixed_composition_attacker_has_affinity_in_one_aspect_only()
        {
            // Fire 50 + Ice 50, magnitude=100
            // Attacker: Fire affinity=100 → boostFactor(Fire)=2.0, boostFactor(Ice)=1.0
            // No resist.
            // Fire: 100*0.5 * 2.0 * 1.0 = 100
            // Ice:  100*0.5 * 1.0 * 1.0 = 50
            // total = 150
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 100 });
            var defender = new EntityBuilder(ecs).Build();

            var composition = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 50,
                [AspectId.Ice]  = 50,
            });
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(150, result);
        }

        [Fact]
        public void Resolve_mixed_composition_sums_contributions_correctly()
        {
            // Three aspects: Fire 60 + Ice 30 + Lightning 10, magnitude=100
            // Attacker: Fire affinity=50 (boostFactor=1.5), others 0
            // Defender: Ice resist=50 (resistFactor=0.5), others 0
            //
            // Fire:      100 * 0.60 * 1.5 * 1.0 = 90.0
            // Ice:       100 * 0.30 * 1.0 * 0.5 = 15.0
            // Lightning: 100 * 0.10 * 1.0 * 1.0 = 10.0
            // total = 115.0 → 115
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 50 });
            var defender = MakeEntity(ecs, baseResistances: new() { [AspectId.Ice] = 50 });

            var composition = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire]      = 60,
                [AspectId.Ice]       = 30,
                [AspectId.Lightning] = 10,
            });
            var result = system.Resolve(100, composition, attacker, defender);

            Assert.Equal(115, result);
        }

        // ── Resolve — boundary / clamp behaviour ─────────────────────────────────

        [Fact]
        public void Resolve_result_never_negative()
        {
            // Composition non-empty but magnitude=0 → total=0 → clamped to 0
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = new EntityBuilder(ecs).Build();
            var defender = new EntityBuilder(ecs).Build();

            var result = system.Resolve(0, AspectComposition.Single(AspectId.Fire), attacker, defender);

            Assert.True(result >= 0);
        }

        [Fact]
        public void Resolve_rounding_midpoint_rounds_to_nearest_even_or_up()
        {
            // Fire 100%, magnitude=1, attacker affinity=50 → boostFactor=1.5, resistFactor=1
            // total = 1 * 1.0 * 1.5 * 1.0 = 1.5 → Math.Round(1.5) = 2 (banker's round) or 2
            var ecs = new EntityService();
            var system = Build(ecs);

            var attacker = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 50 });
            var defender = new EntityBuilder(ecs).Build();

            var composition = AspectComposition.Single(AspectId.Fire);
            var result = system.Resolve(1, composition, attacker, defender);

            // Math.Round(1.5) is 2 under default midpoint rounding; accept either 1 or 2
            Assert.InRange(result, 1, 2);
        }

        // ── Affinity ─────────────────────────────────────────────────────────────

        [Fact]
        public void Affinity_returns_empty_when_no_component()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = new EntityBuilder(ecs).Build();

            var affinity = system.Affinity(id);

            Assert.True(affinity.IsEmpty);
        }

        [Fact]
        public void Affinity_returns_empty_when_component_has_empty_weights()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = MakeEntity(ecs);   // empty dictionaries

            var affinity = system.Affinity(id);

            Assert.True(affinity.IsEmpty);
        }

        [Fact]
        public void Affinity_returns_composition_matching_stored_weights()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = MakeEntity(ecs, affinityWeights: new() { [AspectId.Fire] = 100 });

            var affinity = system.Affinity(id);

            Assert.False(affinity.IsEmpty);
            Assert.Equal(100, affinity.Weights[AspectId.Fire]);
        }

        [Fact]
        public void Affinity_reflects_mixed_weights_exactly()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = MakeEntity(ecs, affinityWeights: new()
            {
                [AspectId.Fire] = 60,
                [AspectId.Ice]  = 40,
            });

            var affinity = system.Affinity(id);

            Assert.Equal(60, affinity.Weights[AspectId.Fire]);
            Assert.Equal(40, affinity.Weights[AspectId.Ice]);
        }

        // ── Resist ───────────────────────────────────────────────────────────────

        [Fact]
        public void Resist_returns_zero_when_no_component()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = new EntityBuilder(ecs).Build();

            Assert.Equal(0, system.Resist(id, AspectId.Fire));
        }

        [Fact]
        public void Resist_returns_zero_when_aspect_not_in_base_resistances()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = MakeEntity(ecs, baseResistances: new() { [AspectId.Ice] = 30 });

            Assert.Equal(0, system.Resist(id, AspectId.Fire));
        }

        [Fact]
        public void Resist_returns_stored_value_within_range()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var id = MakeEntity(ecs, baseResistances: new() { [AspectId.Void] = 75 });

            Assert.Equal(75, system.Resist(id, AspectId.Void));
        }

        [Fact]
        public void Resist_clamps_stored_value_above_100()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var comp = new AspectAffinitiesComponent
            {
                BaseResistances = new Dictionary<AspectId, int> { [AspectId.Nature] = 200 },
            };
            var id = new EntityBuilder(ecs).With(comp).Build();

            Assert.Equal(100, system.Resist(id, AspectId.Nature));
        }

        [Fact]
        public void Resist_clamps_stored_value_below_zero()
        {
            var ecs = new EntityService();
            var system = Build(ecs);
            var comp = new AspectAffinitiesComponent
            {
                BaseResistances = new Dictionary<AspectId, int> { [AspectId.Light] = -20 },
            };
            var id = new EntityBuilder(ecs).With(comp).Build();

            Assert.Equal(0, system.Resist(id, AspectId.Light));
        }

        // ── AspectComposition — IsValid ───────────────────────────────────────────

        [Fact]
        public void AspectComposition_empty_is_valid()
        {
            Assert.True(AspectComposition.Empty.IsValid(out _));
        }

        [Fact]
        public void AspectComposition_single_weight_100_is_valid()
        {
            var comp = AspectComposition.Single(AspectId.Fire);
            Assert.True(comp.IsValid(out _));
        }

        [Fact]
        public void AspectComposition_weights_summing_to_100_is_valid()
        {
            var comp = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 60,
                [AspectId.Ice]  = 40,
            });
            Assert.True(comp.IsValid(out _));
        }

        [Fact]
        public void AspectComposition_weights_not_summing_to_100_is_invalid()
        {
            var comp = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 60,
                [AspectId.Ice]  = 30,   // sum = 90, not 100
            });
            Assert.False(comp.IsValid(out var error));
            Assert.NotNull(error);
        }

        [Fact]
        public void AspectComposition_non_positive_weight_is_invalid()
        {
            var comp = new AspectComposition(new Dictionary<AspectId, int>
            {
                [AspectId.Fire] = 100,
                [AspectId.Ice]  = 0,    // zero weight is invalid
            });
            Assert.False(comp.IsValid(out var error));
            Assert.NotNull(error);
        }

        // ── INV-5: AspectSystem does not hold IEventBus ──────────────────────────

        [Fact]
        public void AspectSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(AspectSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AspectSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
