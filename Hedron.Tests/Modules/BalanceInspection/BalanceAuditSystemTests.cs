using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="BalanceAuditSystem"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md Test plan — band-index
    /// drift delta, within-tolerance exclusion, past-tolerance inclusion, authored Band = 0
    /// exclusion (no assertion, still bucketed), (Tier, Band) bucket counts, empty registry.
    /// </summary>
    public sealed class BalanceAuditSystemTests
    {
        private static (BalanceAuditSystem System, TemplateRegistry Registry) Build(int bandDriftTolerance = 1)
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new BalanceAuditSystem(
                registry,
                new PowerBudgetSystem(PowerBudgetTunables.Default),
                new ItemPowerProjectionSystem(),
                new MobPowerProjectionSystem(),
                PowerBudgetTunables.Default,
                bandDriftTolerance);
            return (system, registry);
        }

        [Fact]
        public void Audit_over_an_empty_registry_returns_an_empty_report()
        {
            var (system, _) = Build();

            var report = system.Audit();

            Assert.Empty(report.Drifted);
            Assert.Empty(report.BucketCounts);
        }

        [Fact]
        public void Audit_flags_an_item_whose_drift_exceeds_tolerance()
        {
            var (system, registry) = Build();
            var item = new ItemTemplate("item.drifted.test")
            {
                Name = "Drifted Blade",
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };
            registry.Register(item.BlueprintId, item);

            var report = system.Audit();

            var entry = Assert.Single(report.Drifted);
            Assert.Equal(BalanceAuditKind.Item, entry.Kind);
            Assert.Equal("item.drifted.test", entry.BlueprintId);
            Assert.Equal(2, entry.AuthoredTier);
            Assert.Equal(2, entry.AuthoredBand);
            Assert.True(entry.Drift > 1); // default bandDriftTolerance from Build()
        }

        [Fact]
        public void Audit_excludes_an_item_within_drift_tolerance()
        {
            var (system, registry) = Build();
            // No stat bonuses, tier/band both 0 — computed classification lands on (0, 1), matching
            // the authored tag exactly (drift 0), so it must not appear in Drifted.
            var item = new ItemTemplate("item.within-tolerance.test")
            {
                Name = "Plain Dagger",
                Tier = 0,
                Band = 1,
            };
            registry.Register(item.BlueprintId, item);

            var report = system.Audit();

            Assert.Empty(report.Drifted);
        }

        [Fact]
        public void Audit_excludes_unbanded_item_from_drift_but_still_buckets_it()
        {
            var (system, registry) = Build();
            var item = new ItemTemplate("item.unbanded.test")
            {
                Name = "Unbanded Trinket",
                Tier = 0,
                Band = 0,
            };
            registry.Register(item.BlueprintId, item);

            var report = system.Audit();

            Assert.Empty(report.Drifted);
            Assert.Equal(1, report.BucketCounts[(0, 1)]);
        }

        [Fact]
        public void Audit_flags_a_mob_whose_drift_exceeds_tolerance_via_the_template_projection()
        {
            var (system, registry) = Build();
            var mob = new MobTemplate("mob.drifted.test")
            {
                Name = "Overtagged Goblin",
                Body = 12,
                MaxHp = 80,
                Tier = 3,
                Band = 3,
            };
            registry.Register(mob.BlueprintId, mob);

            var report = system.Audit();

            var entry = Assert.Single(report.Drifted);
            Assert.Equal(BalanceAuditKind.Mob, entry.Kind);
            Assert.Equal("mob.drifted.test", entry.BlueprintId);
            Assert.Equal(3, entry.AuthoredTier);
            Assert.Equal(3, entry.AuthoredBand);
            Assert.True(entry.Drift > 1); // default bandDriftTolerance from Build()
        }

        [Fact]
        public void Audit_flagged_set_changes_with_the_injected_band_drift_tolerance()
        {
            // Same item, two systems built with different injected tolerances (sim-1) — the
            // flagged set must differ, proving the tolerance is real injected data, not a
            // compiled constant baked into the audit logic.
            var item = new ItemTemplate("item.tolerance-swap.test")
            {
                Name = "Tolerance Swap Blade",
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };

            var (looseSystem, looseRegistry) = Build(bandDriftTolerance: 100);
            looseRegistry.Register(item.BlueprintId, item);
            Assert.Empty(looseSystem.Audit().Drifted);

            var (tightSystem, tightRegistry) = Build(bandDriftTolerance: 0);
            tightRegistry.Register(item.BlueprintId, item);
            Assert.Single(tightSystem.Audit().Drifted);
        }

        [Fact]
        public void Audit_buckets_every_template_by_its_computed_cell()
        {
            var (system, registry) = Build();
            var item1 = new ItemTemplate("item.a.test") { Name = "A", Tier = 0, Band = 1 };
            var item2 = new ItemTemplate("item.b.test") { Name = "B", Tier = 0, Band = 0 };
            registry.Register(item1.BlueprintId, item1);
            registry.Register(item2.BlueprintId, item2);

            var report = system.Audit();

            Assert.Equal(2, report.BucketCounts[(0, 1)]);
            Assert.Equal(2, report.BucketCounts.Values.Sum());
        }
    }
}
