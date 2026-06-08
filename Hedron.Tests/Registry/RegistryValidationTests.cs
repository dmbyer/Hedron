using System;
using System.Collections.Generic;
using System.Threading;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Registry
{
    /// <summary>
    /// Tier 5 — validation tests for <see cref="RegistryValidationBootstrap"/>.
    ///
    /// Each test constructs registries with deliberate bad data in-test, runs the
    /// validator, and asserts throws vs. no-throw — no assertions on prose output.
    ///
    /// Coverage: dangling ability→effect ref, dangling ability→aspect ref,
    /// invalid AspectComposition, bad CharacterDefaults:StartingAbilities, and
    /// the happy path (valid registry set passes).
    /// </summary>
    public sealed class RegistryValidationTests
    {
        // ── Test-local registry stubs ─────────────────────────────────────────

        /// <summary>
        /// In-test subclass of <see cref="DefinitionRegistry{TKey,TDef}"/> that accepts
        /// an arbitrary row set so tests can inject deliberately bad data without touching
        /// the production registries.
        /// </summary>
        private sealed class StubAbilityRegistry
            : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry(IEnumerable<AbilityDefinition> rows)
                : base(rows, d => d.Id) { }
        }

        private sealed class StubEffectRegistry
            : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry(IEnumerable<EffectDefinition> rows)
                : base(rows, d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry
            : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry(IEnumerable<AspectDefinition> rows)
                : base(rows, d => d.Id) { }
        }

        // ── Canonical valid rows ──────────────────────────────────────────────

        private static readonly EffectDefinition ValidEffect = new EffectDefinition(
            "hit", EffectKind.Instant,
            new EffectParams(ScoreId.HpCurrent, -10),
            EffectCategory.Debuff, "fixed", 0f,
            StackPolicy.Replace, EffectPhase.Normal);

        private static readonly AspectDefinition ValidAspect = new AspectDefinition(
            AspectId.Fire, "Fire", "Searing flame.", AspectCategory.Elemental);

        private static readonly AbilityDefinition ValidAbility = new AbilityDefinition(
            "strike", "Strike",
            AbilityKind.Skill, Activation.Active, Targeting.Target,
            Costs: new List<ResourceCost>(),
            Effects: new List<string> { "hit" },
            CooldownSeconds: 0f,
            Aspect: AspectComposition.Single(AspectId.Fire));

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Constructs a <see cref="RegistryValidationBootstrap"/> and calls
        /// <see cref="IHostedService.StartAsync"/> synchronously.
        /// </summary>
        private static void RunValidator(
            IAbilityRegistry abilities,
            IEffectRegistry effects,
            IAspectRegistry aspects,
            IConfiguration configuration)
        {
            var bootstrap = new RegistryValidationBootstrap(
                abilities,
                effects,
                aspects,
                configuration,
                NullLogger<RegistryValidationBootstrap>.Instance);

            bootstrap.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds a minimal <see cref="IConfiguration"/> with the given StartingAbilities array.
        /// </summary>
        private static IConfiguration BuildConfig(params string[] startingAbilities)
        {
            var pairs = new Dictionary<string, string?>();
            for (int i = 0; i < startingAbilities.Length; i++)
                pairs[$"CharacterDefaults:StartingAbilities:{i}"] = startingAbilities[i];

            return new ConfigurationBuilder()
                .AddInMemoryCollection(pairs)
                .Build();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Case 1: Dangling ability → effect reference
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An AbilityDefinition referencing an effect id that is absent from the
        /// EffectRegistry must cause validation to throw.
        /// </summary>
        [Fact]
        public void Dangling_ability_to_effect_reference_throws()
        {
            var ability = new AbilityDefinition(
                "bad_ability", "Bad",
                AbilityKind.Skill, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "nonexistent_effect" },   // dangling
                CooldownSeconds: 0f);

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(Array.Empty<EffectDefinition>());  // empty
            var aspects   = new StubAspectRegistry(Array.Empty<AspectDefinition>());
            var config    = BuildConfig();

            Assert.Throws<InvalidOperationException>(
                () => RunValidator(abilities, effects, aspects, config));
        }

        // ═════════════════════════════════════════════════════════════════════
        // Case 2: Dangling ability → aspect reference
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An AbilityDefinition whose AspectComposition references an AspectId not present
        /// in the AspectRegistry must cause validation to throw.
        /// </summary>
        [Fact]
        public void Dangling_ability_to_aspect_reference_throws()
        {
            // The composition itself is valid (sums to 100), but AspectId.Void
            // is not registered in the empty AspectRegistry.
            var composition = new AspectComposition(
                new Dictionary<AspectId, int> { [AspectId.Void] = 100 });

            var ability = new AbilityDefinition(
                "arcane_strike", "Arcane Strike",
                AbilityKind.Spell, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: composition);   // AspectId.Void will be dangling

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(Array.Empty<AspectDefinition>());  // empty — Void not registered
            var config    = BuildConfig();

            Assert.Throws<InvalidOperationException>(
                () => RunValidator(abilities, effects, aspects, config));
        }

        // ═════════════════════════════════════════════════════════════════════
        // Case 3: Invalid AspectComposition — weights do not sum to 100
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An AspectComposition whose weights sum to something other than 100 is invalid.
        /// Validation must throw even if the aspect ids themselves exist in the registry.
        /// </summary>
        [Fact]
        public void Invalid_AspectComposition_weights_not_100_throws()
        {
            // Weights sum to 60, not 100 — IsValid() will return false.
            var badComposition = new AspectComposition(
                new Dictionary<AspectId, int> { [AspectId.Fire] = 60 });

            var ability = new AbilityDefinition(
                "flame_strike", "Flame Strike",
                AbilityKind.Spell, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: badComposition);

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig();

            Assert.Throws<InvalidOperationException>(
                () => RunValidator(abilities, effects, aspects, config));
        }

        /// <summary>
        /// An AspectComposition with a non-positive weight is invalid.
        /// Validation must throw.
        /// </summary>
        [Fact]
        public void Invalid_AspectComposition_non_positive_weight_throws()
        {
            // Weight of 0 — IsValid() catches non-positive weights.
            var badComposition = new AspectComposition(
                new Dictionary<AspectId, int> { [AspectId.Fire] = 0 });

            var ability = new AbilityDefinition(
                "null_strike", "Null Strike",
                AbilityKind.Spell, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: badComposition);

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig();

            Assert.Throws<InvalidOperationException>(
                () => RunValidator(abilities, effects, aspects, config));
        }

        // ═════════════════════════════════════════════════════════════════════
        // Case 4: Bad CharacterDefaults:StartingAbilities entry
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A CharacterDefaults:StartingAbilities entry that names an ability id not present
        /// in the AbilityRegistry must cause validation to throw.
        /// </summary>
        [Fact]
        public void Bad_StartingAbilities_config_entry_throws()
        {
            // Ability registry is empty — "kick" does not exist.
            var abilities = new StubAbilityRegistry(Array.Empty<AbilityDefinition>());
            var effects   = new StubEffectRegistry(Array.Empty<EffectDefinition>());
            var aspects   = new StubAspectRegistry(Array.Empty<AspectDefinition>());
            var config    = BuildConfig("kick");   // "kick" is dangling

            Assert.Throws<InvalidOperationException>(
                () => RunValidator(abilities, effects, aspects, config));
        }

        // ═════════════════════════════════════════════════════════════════════
        // Case 5: Valid registry set passes
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A well-formed set of registries (all refs resolve, composition normalizes,
        /// StartingAbilities all exist) must pass validation without throwing.
        /// </summary>
        [Fact]
        public void Valid_registry_set_passes_without_throwing()
        {
            var abilities = new StubAbilityRegistry(new[] { ValidAbility });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig("strike");   // "strike" exists in abilities

            // Must not throw.
            RunValidator(abilities, effects, aspects, config);
        }

        /// <summary>
        /// A valid registry set with no StartingAbilities configured must also pass.
        /// </summary>
        [Fact]
        public void Valid_registry_set_with_empty_StartingAbilities_passes()
        {
            var abilities = new StubAbilityRegistry(new[] { ValidAbility });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig();   // no starting abilities

            // Must not throw.
            RunValidator(abilities, effects, aspects, config);
        }

        /// <summary>
        /// An ability with an empty AspectComposition (untyped) and valid effect refs passes.
        /// Empty composition is explicitly valid per AspectComposition.IsValid().
        /// </summary>
        [Fact]
        public void Ability_with_empty_AspectComposition_passes()
        {
            var ability = new AbilityDefinition(
                "plain_hit", "Plain Hit",
                AbilityKind.Skill, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: AspectComposition.Empty);

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig();

            // Must not throw — empty composition is valid.
            RunValidator(abilities, effects, aspects, config);
        }

        /// <summary>
        /// An ability with null Aspect (no composition at all) and valid effect refs passes.
        /// </summary>
        [Fact]
        public void Ability_with_null_Aspect_passes()
        {
            var ability = new AbilityDefinition(
                "raw_hit", "Raw Hit",
                AbilityKind.Skill, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: null);

            var abilities = new StubAbilityRegistry(new[] { ability });
            var effects   = new StubEffectRegistry(new[] { ValidEffect });
            var aspects   = new StubAspectRegistry(new[] { ValidAspect });
            var config    = BuildConfig();

            // Must not throw — null aspect skips the aspect validation block.
            RunValidator(abilities, effects, aspects, config);
        }
    }
}
