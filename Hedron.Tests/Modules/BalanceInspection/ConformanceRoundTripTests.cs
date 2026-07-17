using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 3 — flow round-trip for the conformance fitter over a real
    /// <see cref="ContentDefinitionCatalog"/> on a temp content directory (mirrors
    /// <c>Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs</c>'s harness): author out-of-band
    /// YAML → <see cref="ITemplateConformanceSystem.Preview"/> → <c>ApplyAsync</c> → re-<c>Load</c>
    /// from disk → re-project/classify in range. Catches YAML field-fidelity regressions a fake
    /// catalog can't (docs/implementation-plans/conformance-tooling.md Test plan Tier 3).
    /// </summary>
    public sealed class ConformanceRoundTripTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private sealed class StubAbilityRegistry
            : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry() : base(Array.Empty<AbilityDefinition>(), d => d.Id) { }
        }

        private sealed class StubEffectRegistry
            : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry() : base(Array.Empty<EffectDefinition>(), d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry
            : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry() : base(Array.Empty<AspectDefinition>(), d => d.Id) { }
        }

        private (ContentDefinitionCatalog Catalog, TemplateConformanceSystem Conformance) NewFixture()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-conformance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);

            var options = Options.Create(new WorldOptions { ContentDirectory = dir });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });

            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var validator = new ContentValidator(
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs);

            var catalog = new ContentDefinitionCatalog(
                serializer,
                validator,
                registry,
                new AreaContentWriter(options),
                new RoomContentWriter(options),
                new ItemContentWriter(options),
                new MobContentWriter(options),
                options,
                NullLogger<ContentDefinitionCatalog>.Instance);

            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var itemProjection = new ItemPowerProjectionSystem();
            var mobProjection = new MobPowerProjectionSystem();
            var audit = new BalanceAuditSystem(
                registry, powerBudget, itemProjection, mobProjection,
                PowerBudgetTunables.Default, bandDriftTolerance: 1);

            var conformance = new TemplateConformanceSystem(catalog, powerBudget, itemProjection, mobProjection, audit);
            return (catalog, conformance);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        [Fact]
        public async Task Item_authored_out_of_band_fits_and_the_applied_YAML_reclassifies_in_range()
        {
            var (catalog, conformance) = NewFixture();
            var def = catalog.CreateNew(ContentKind.Item, "Round-Trip Blade");
            var item = (ItemTemplate)def.Template;
            item.Tier = 2;
            item.Band = 2;
            item.StatBonuses = new List<EquipmentStatBonus>
            {
                new(ScoreId.AttackPower, 15),
                new(ScoreId.Defense, 5),
            };
            var writeResult = await catalog.SaveAsync(def);
            Assert.True(writeResult.Success);

            var preview = conformance.Preview(BalanceAuditKind.Item, item.BlueprintId);
            Assert.Equal(ConformanceStatus.Fitted, preview.Status);

            var applyResult = await conformance.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);
            Assert.True(applyResult.Success);

            var reloaded = catalog.Load(ContentKind.Item, item.BlueprintId);
            var reloadedItem = Assert.IsType<ItemTemplate>(reloaded!.Template);

            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var reprojected = powerBudget.Estimate(new ItemPowerProjectionSystem().Project(reloadedItem), reloadedItem.Tier);
            Assert.Equal(new PowerBand(2, 2), powerBudget.Classify(reprojected));
        }

        [Fact]
        public async Task Mob_authored_out_of_band_fits_and_the_applied_YAML_reclassifies_in_range()
        {
            var (catalog, conformance) = NewFixture();
            var def = catalog.CreateNew(ContentKind.Mob, "Round-Trip Goblin");
            var mob = (MobTemplate)def.Template;
            mob.Tier = 3;
            mob.Band = 2;
            mob.Body = 12;
            mob.MaxHp = 80;
            var writeResult = await catalog.SaveAsync(def);
            Assert.True(writeResult.Success);

            var preview = conformance.Preview(BalanceAuditKind.Mob, mob.BlueprintId);
            Assert.Equal(ConformanceStatus.Fitted, preview.Status);

            var applyResult = await conformance.ApplyAsync(BalanceAuditKind.Mob, mob.BlueprintId);
            Assert.True(applyResult.Success);

            var reloaded = catalog.Load(ContentKind.Mob, mob.BlueprintId);
            var reloadedMob = Assert.IsType<MobTemplate>(reloaded!.Template);

            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var reprojected = powerBudget.Estimate(new MobPowerProjectionSystem().Project(reloadedMob), reloadedMob.Tier);
            Assert.Equal(new PowerBand(3, 2), powerBudget.Classify(reprojected));
        }
    }
}
