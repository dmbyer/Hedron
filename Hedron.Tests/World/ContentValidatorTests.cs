using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Mobs.Templates;
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
            EntityService? ecs = null,
            ITemplateRegistry? templateRegistry = null)
        {
            var entityService = ecs ?? new EntityService();
            return new ContentValidator(
                abilities ?? new StubAbilityRegistry(Array.Empty<AbilityDefinition>()),
                effects ?? new StubEffectRegistry(Array.Empty<EffectDefinition>()),
                aspects ?? new StubAspectRegistry(new[] { FireAspect }),
                entityService,
                templateRegistry ?? new TemplateRegistry(entityService));
        }

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

        // ── Validate: mob currency-loot ranges (authoring-api-surface WP1) ──────────
        //
        // Fail-fast validation, so it is in 07-testing.md's always-test column. The rule lives here
        // rather than in MobEditor.razor so every surface that writes a mob — the editor, the bulk
        // generator, and the JSON endpoints — is refused the same malformed range.

        private static MobTemplate MobWithCoinLoot(int min, int max) =>
            new("mob.test")
            {
                Name = "Test",
                CurrencyLoot = new Dictionary<CurrencyId, (int Min, int Max)>
                {
                    [CurrencyId.Coin] = (min, max),
                },
            };

        [Fact]
        public void Validate_MobWithNoCurrencyLoot_IsValid()
        {
            Assert.True(Build().Validate(new MobTemplate("mob.test") { Name = "Test" }).IsValid);
        }

        [Fact]
        public void Validate_MobWithWellFormedCoinLoot_IsValid()
        {
            Assert.True(Build().Validate(MobWithCoinLoot(5, 25)).IsValid);
        }

        [Fact]
        public void Validate_MobWithEqualMinAndMaxCoinLoot_IsValid()
        {
            Assert.True(Build().Validate(MobWithCoinLoot(10, 10)).IsValid);
        }

        [Fact]
        public void Validate_MobWithInvertedCoinLootRange_FailsFast()
        {
            var report = Build().Validate(MobWithCoinLoot(50, 10));

            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Contains("mob.test") && e.Contains("exceed"));
        }

        [Fact]
        public void Validate_MobWithNegativeCoinLoot_FailsFast()
        {
            var report = Build().Validate(MobWithCoinLoot(-5, 10));

            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Contains("negative"));
        }

        [Fact]
        public void Validate_MobWithNegativeMax_FailsFast()
        {
            var report = Build().Validate(MobWithCoinLoot(0, -1));

            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Contains("negative"));
        }

        // ── ValidateRegistry: coordinate-collision warning (world-editor-grid Postcondition 9) ──

        [Fact]
        public void ValidateRegistry_SameAreaSameCell_ProducesWarning_ReportStillValid()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            registry.Register("room.a", new RoomTemplate("room.a") { AreaId = "area.1", X = 1, Y = 1, Z = 0 });
            registry.Register("room.b", new RoomTemplate("room.b") { AreaId = "area.1", X = 1, Y = 1, Z = 0 });

            var validator = Build(ecs: ecs, templateRegistry: registry);
            var report = validator.ValidateRegistry(Array.Empty<string>());

            Assert.True(report.IsValid);
            Assert.Empty(report.Errors);
            Assert.Contains(report.Warnings, w => w.Contains("room.a") && w.Contains("room.b"));
        }

        [Fact]
        public void ValidateRegistry_DifferentArea_SameCell_NoWarning()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            registry.Register("room.a", new RoomTemplate("room.a") { AreaId = "area.1", X = 1, Y = 1, Z = 0 });
            registry.Register("room.b", new RoomTemplate("room.b") { AreaId = "area.2", X = 1, Y = 1, Z = 0 });

            var validator = Build(ecs: ecs, templateRegistry: registry);
            var report = validator.ValidateRegistry(Array.Empty<string>());

            Assert.True(report.IsValid);
            Assert.Empty(report.Warnings);
        }

        [Fact]
        public void ValidateRegistry_SameArea_DifferentZ_NoWarning()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            registry.Register("room.a", new RoomTemplate("room.a") { AreaId = "area.1", X = 1, Y = 1, Z = 0 });
            registry.Register("room.b", new RoomTemplate("room.b") { AreaId = "area.1", X = 1, Y = 1, Z = 1 });

            var validator = Build(ecs: ecs, templateRegistry: registry);
            var report = validator.ValidateRegistry(Array.Empty<string>());

            Assert.True(report.IsValid);
            Assert.Empty(report.Warnings);
        }

        [Fact]
        public void ValidateRegistry_NoCollisions_EmptyWarnings_AndOkIsValid()
        {
            Assert.True(Build().ValidateRegistry(Array.Empty<string>()).IsValid);
            Assert.Empty(Build().ValidateRegistry(Array.Empty<string>()).Warnings);
        }

        // ── ValidateBlueprintId (blueprint-id-editing, OQ2) ──────────────────────────

        [Theory]
        [InlineData("room.crossroads")]
        [InlineData("area.starter_road")]
        [InlineData("item.sword-of-truth")]
        [InlineData("mob.Goblin.King")]
        public void ValidateBlueprintId_AcceptsFilenameSafe(string id)
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, id);

            Assert.True(report.IsValid);
        }

        [Theory]
        [InlineData("room/crossroads")]
        [InlineData("room\\crossroads")]
        public void ValidateBlueprintId_RejectsPathSeparators(string id)
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, id);

            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void ValidateBlueprintId_RejectsDotDot()
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, "room..crossroads");

            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void ValidateBlueprintId_RejectsEmpty()
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, string.Empty);

            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void ValidateBlueprintId_RejectsIllegalCharacter()
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, "room crossroads!");

            Assert.False(report.IsValid);
            Assert.NotEmpty(report.Errors);
        }

        [Fact]
        public void ValidateBlueprintId_WarnsOnKindPrefixMismatch()
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, "not-a-room-prefix");

            Assert.True(report.IsValid);
            Assert.NotEmpty(report.Warnings);
        }

        [Fact]
        public void ValidateBlueprintId_NoWarning_WhenPrefixMatches()
        {
            var report = Build().ValidateBlueprintId(ContentKind.Room, "room.crossroads");

            Assert.True(report.IsValid);
            Assert.Empty(report.Warnings);
        }
    }
}
