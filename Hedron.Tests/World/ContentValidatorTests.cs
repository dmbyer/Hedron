using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Unit tests for <see cref="ContentValidator"/> — the rules factored out of
    /// <c>RegistryValidationBootstrap</c>. These assert the validator returns a structured
    /// <see cref="ValidationReport"/> (never throws), covering both call modes: the whole-registry
    /// sweep and the single in-memory definition check (the editor's per-edit path).
    /// </summary>
    public sealed class ContentValidatorTests
    {
        // ── Test-local registry stubs (mirror RegistryValidationTests) ────────────

        private sealed class StubAbilityRegistry
            : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry(IEnumerable<AbilityDefinition> rows) : base(rows, d => d.Id) { }
        }

        private sealed class StubEffectRegistry
            : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry(IEnumerable<EffectDefinition> rows) : base(rows, d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry
            : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry(IEnumerable<AspectDefinition> rows) : base(rows, d => d.Id) { }
        }

        private static readonly EffectDefinition ValidEffect = new EffectDefinition(
            "hit", EffectKind.Instant,
            new EffectParams(ScoreId.HpCurrent, -10),
            EffectCategory.Debuff, "fixed", 0f,
            StackPolicy.Replace, EffectPhase.Normal);

        private static readonly AspectDefinition FireAspect = new AspectDefinition(
            AspectId.Fire, "Fire", "Searing flame.", AspectCategory.Elemental);

        private static ContentValidator Build(
            IAbilityRegistry? abilities = null,
            IEffectRegistry? effects = null,
            IAspectRegistry? aspects = null,
            EntityService? ecs = null)
            => new ContentValidator(
                abilities ?? new StubAbilityRegistry(Array.Empty<AbilityDefinition>()),
                effects ?? new StubEffectRegistry(Array.Empty<EffectDefinition>()),
                aspects ?? new StubAspectRegistry(new[] { FireAspect }),
                ecs ?? new EntityService());

        // ── ValidateRegistry mode ─────────────────────────────────────────────────

        [Fact]
        public void ValidateRegistry_AcceptsValidContent()
        {
            var ability = new AbilityDefinition(
                "strike", "Strike",
                AbilityKind.Skill, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "hit" },
                CooldownSeconds: 0f,
                Aspect: AspectComposition.Single(AspectId.Fire));

            var validator = Build(
                new StubAbilityRegistry(new[] { ability }),
                new StubEffectRegistry(new[] { ValidEffect }),
                new StubAspectRegistry(new[] { FireAspect }));

            var report = validator.ValidateRegistry(new[] { "strike" });

            Assert.True(report.IsValid);
            Assert.Empty(report.Errors);
        }

        [Fact]
        public void ValidateRegistry_ReturnsStructuredErrors_OnBrokenCrossRef_DoesNotThrow()
        {
            var ability = new AbilityDefinition(
                "bad", "Bad",
                AbilityKind.Skill, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "nonexistent_effect" },
                CooldownSeconds: 0f);

            var validator = Build(
                new StubAbilityRegistry(new[] { ability }),
                new StubEffectRegistry(Array.Empty<EffectDefinition>()),
                new StubAspectRegistry(Array.Empty<AspectDefinition>()));

            // The editor needs structured errors, not an exception — that is the whole point of
            // the factoring (the bootstrap, not the validator, decides to throw).
            var report = validator.ValidateRegistry(Array.Empty<string>());

            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void ValidateRegistry_ReportsDanglingStartingAbility()
        {
            var validator = Build();

            var report = validator.ValidateRegistry(new[] { "kick" }); // empty ability registry

            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Contains("kick"));
        }

        // ── Validate (single-definition) mode ──────────────────────────────────────

        [Fact]
        public void Validate_AcceptsValidAreaDefinition()
        {
            var area = new AreaTemplate("area.test")
            {
                Name = "Test",
                AspectAffinities = new Dictionary<AspectId, int> { [AspectId.Fire] = 100 },
            };

            var validator = Build(aspects: new StubAspectRegistry(new[] { FireAspect }));

            Assert.True(validator.Validate(area).IsValid);
        }

        [Fact]
        public void Validate_RejectsAreaWithInvalidComposition()
        {
            // Weights sum to 60, not 100.
            var area = new AreaTemplate("area.test")
            {
                Name = "Test",
                AspectAffinities = new Dictionary<AspectId, int> { [AspectId.Fire] = 60 },
            };

            var validator = Build(aspects: new StubAspectRegistry(new[] { FireAspect }));

            var report = validator.Validate(area);
            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void Validate_AreaWithNoAffinities_IsValid()
        {
            var area = new AreaTemplate("area.test") { Name = "Test" };
            Assert.True(Build().Validate(area).IsValid);
        }

        [Fact]
        public void Validate_RoomDefinition_HasNoSingleDefinitionRulesYet_IsValid()
        {
            var room = new RoomTemplate("room.test") { Name = "Test" };
            Assert.True(Build().Validate(room).IsValid);
        }
    }
}
