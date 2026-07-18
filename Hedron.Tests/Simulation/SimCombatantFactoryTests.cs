using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Ascension.Components;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 1 — <see cref="SimCombatantFactory"/> two-phase resolution (Postcondition 6).</summary>
    public sealed class SimCombatantFactoryTests
    {
        private sealed class FakeCatalog : IContentDefinitionCatalog
        {
            private readonly Dictionary<string, ContentDefinition> _mobs = new();

            public void AddMob(MobTemplate template) => _mobs[template.BlueprintId] = new ContentDefinition(ContentKind.Mob, template);

            public IReadOnlyList<ContentSummary> List(ContentKind kind) => throw new NotImplementedException();
            public IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId) => throw new NotImplementedException();

            public ContentDefinition? Load(ContentKind kind, string blueprintId) =>
                kind == ContentKind.Mob && _mobs.TryGetValue(blueprintId, out var def) ? def : null;

            public Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentWriteResult> SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct = default) => throw new NotImplementedException();
            public Task<ContentDeleteResult> DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct = default) => throw new NotImplementedException();
            public ContentDefinition CreateNew(ContentKind kind, string name) => throw new NotImplementedException();
            public Task<ContentWriteResult> RemoveRoomExitAsync(string roomBlueprintId, Direction direction, bool bidirectional, CancellationToken ct = default) => throw new NotImplementedException();
        }

        private static (SimCombatantFactory Factory, FakeCatalog Catalog, IBalanceStandardsRegistry Registry, SandboxWorld World) NewFixture()
        {
            var catalog = new FakeCatalog();
            var abilityRegistry = new AbilityRegistry();

            var document = new BalanceStandardsDocument(
                PowerBudgetTunables.Default,
                BandDriftTolerance: 1,
                Outcomes: BalanceStandardsDefaults.Outcomes,
                Cells: new[]
                {
                    new BalanceStandard(2, 2, new ReferenceBuildDefinition(
                        new Dictionary<ScoreId, int> { [ScoreId.AttackPower] = 6 },
                        new List<string> { "kick" }), null),
                });
            var registry = new BalanceStandardsRegistry(document);

            var factory = new SimCombatantFactory(catalog, registry, abilityRegistry);

            var worldFactory = new SandboxWorldFactory(
                abilityRegistry, new EffectRegistry(), new PowerBudgetSystem(PowerBudgetTunables.Default),
                Options.Create(new DeathOptions()));
            var world = worldFactory.Create(new FakeRandom(1));

            return (factory, catalog, registry, world);
        }

        // ── Reference build ──────────────────────────────────────────────────

        [Fact]
        public void Resolve_ReferenceBuild_MaterializedScores_MatchSnapshotPlusTierBaseline()
        {
            var (factory, _, registry, world) = NewFixture();
            var spec = new CombatantSpec(CombatantSourceKind.ReferenceBuild, "melee-only", Tier: 2, Band: 2);

            var resolved = factory.Resolve(spec);
            var entityId = factory.Materialize(world, resolved);

            var snapshot = registry.ReferenceSnapshot(2, 2);
            var tunables = PowerBudgetTunables.Default;

            foreach (var (score, baseValue) in snapshot.Scores)
            {
                var tierBonus = tunables.TrackedScores.Contains(score) ? tunables.TierBaselineStep * 2 : 0;
                Assert.Equal(baseValue + tierBonus, world.Stats.Get(entityId, score));
            }

            Assert.True(world.Abilities.IsKnown(entityId, "kick"));
            Assert.Equal(new PowerBand(2, 2), resolved.Cell);
        }

        [Fact]
        public void Resolve_ReferenceBuild_MissingTierOrBand_Throws()
        {
            var (factory, _, _, _) = NewFixture();
            var spec = new CombatantSpec(CombatantSourceKind.ReferenceBuild, "melee-only", Tier: 2, Band: null);
            Assert.Throws<InvalidOperationException>(() => factory.Resolve(spec));
        }

        // ── Mob template ──────────────────────────────────────────────────────

        [Fact]
        public void Resolve_MobTemplate_MirrorsTemplateAttributesPoolsTierBand()
        {
            var (factory, catalog, _, world) = NewFixture();
            var template = new MobTemplate("mob.test.goblin")
            {
                Name = "goblin",
                Body = 14,
                Mind = 8,
                Spirit = 8,
                Attunement = 8,
                MaxHp = 60,
                MaxMana = 20,
                MaxStamina = 30,
                MaxAstra = 5,
                Tier = 1,
                Band = 2,
            };
            catalog.AddMob(template);

            var spec = new CombatantSpec(CombatantSourceKind.MobTemplate, "melee-only", MobBlueprintId: "mob.test.goblin");
            var resolved = factory.Resolve(spec);
            var entityId = factory.Materialize(world, resolved);

            Assert.Equal(14, world.EntityService.Get<AttributesComponent>(entityId).Body);
            Assert.Equal(60, world.EntityService.Get<PoolsComponent>(entityId).MaxHp);
            Assert.Equal(1, world.EntityService.Get<AscensionComponent>(entityId).Tier);
            Assert.Equal(new PowerBand(1, 2), resolved.Cell);
        }

        [Fact]
        public void Resolve_UnbandedMobTemplate_WithSpecAnnotation_ResolvesAnnotationAsCell()
        {
            var (factory, catalog, _, _) = NewFixture();
            var template = new MobTemplate("mob.test.unbanded")
            {
                Name = "wisp", Body = 10, Tier = 0, Band = 0,
            };
            catalog.AddMob(template);

            var spec = new CombatantSpec(
                CombatantSourceKind.MobTemplate, "melee-only", MobBlueprintId: "mob.test.unbanded", Tier: 3, Band: 2);
            var resolved = factory.Resolve(spec);

            Assert.Equal(new PowerBand(3, 2), resolved.Cell);
        }

        [Fact]
        public void Resolve_BandedMobTemplate_IgnoresSpecAnnotation_AuthoredTagWins()
        {
            var (factory, catalog, _, _) = NewFixture();
            var template = new MobTemplate("mob.test.banded")
            {
                Name = "guard", Body = 10, Tier = 1, Band = 2,
            };
            catalog.AddMob(template);

            var spec = new CombatantSpec(
                CombatantSourceKind.MobTemplate, "melee-only", MobBlueprintId: "mob.test.banded", Tier: 5, Band: 3);
            var resolved = factory.Resolve(spec);

            Assert.Equal(new PowerBand(1, 2), resolved.Cell);
        }

        [Fact]
        public void Resolve_UnbandedMobTemplate_NoSpecAnnotation_ResolvesNoCell()
        {
            var (factory, catalog, _, _) = NewFixture();
            var template = new MobTemplate("mob.test.plain")
            {
                Name = "rat", Body = 10, Tier = 0, Band = 0,
            };
            catalog.AddMob(template);

            var spec = new CombatantSpec(CombatantSourceKind.MobTemplate, "melee-only", MobBlueprintId: "mob.test.plain");
            var resolved = factory.Resolve(spec);

            Assert.Null(resolved.Cell);
        }

        [Fact]
        public void Resolve_UnknownMobBlueprintId_Throws()
        {
            var (factory, _, _, _) = NewFixture();
            var spec = new CombatantSpec(CombatantSourceKind.MobTemplate, "melee-only", MobBlueprintId: "mob.does.not.exist");
            Assert.Throws<InvalidOperationException>(() => factory.Resolve(spec));
        }

        // ── Inline ─────────────────────────────────────────────────────────────

        [Fact]
        public void Resolve_Inline_StampsDeclaredValuesAndLearnsKit()
        {
            var (factory, _, _, world) = NewFixture();
            var inline = new InlineStatBlock(
                new Dictionary<ScoreId, int> { [ScoreId.Body] = 16, [ScoreId.HpMax] = 80 },
                new List<string> { "kick" });
            var spec = new CombatantSpec(CombatantSourceKind.Inline, "melee-only", Inline: inline);

            var resolved = factory.Resolve(spec);
            var entityId = factory.Materialize(world, resolved);

            Assert.Equal(16, world.EntityService.Get<AttributesComponent>(entityId).Body);
            Assert.Equal(80, world.EntityService.Get<PoolsComponent>(entityId).MaxHp);
            Assert.True(world.Abilities.IsKnown(entityId, "kick"));
        }

        [Fact]
        public void Resolve_Inline_MissingInlineBlock_Throws()
        {
            var (factory, _, _, _) = NewFixture();
            var spec = new CombatantSpec(CombatantSourceKind.Inline, "melee-only");
            Assert.Throws<InvalidOperationException>(() => factory.Resolve(spec));
        }

        [Fact]
        public void Resolve_UnknownAbilityIdInInlineKit_Throws()
        {
            var (factory, _, _, _) = NewFixture();
            var inline = new InlineStatBlock(new Dictionary<ScoreId, int>(), new List<string> { "not-a-real-ability" });
            var spec = new CombatantSpec(CombatantSourceKind.Inline, "melee-only", Inline: inline);
            Assert.Throws<InvalidOperationException>(() => factory.Resolve(spec));
        }

        [Fact]
        public void Resolve_UnknownScoreIdInInlineScores_Throws()
        {
            var (factory, _, _, _) = NewFixture();
            var badScores = new Dictionary<ScoreId, int> { [(ScoreId)999] = 5 };
            var inline = new InlineStatBlock(badScores, new List<string>());
            var spec = new CombatantSpec(CombatantSourceKind.Inline, "melee-only", Inline: inline);
            Assert.Throws<InvalidOperationException>(() => factory.Resolve(spec));
        }
    }
}
