using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="TemplateConformanceSystem"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/conformance-tooling.md Test plan Tier 1 items
    /// 1-9 — item fit, mob fit, midpoint/determinism, rounding-correction convergence (and its
    /// non-convergent counterpart), AlreadyInRange, NotFittable guards, apply re-derives from
    /// disk, validation-refusal propagation, bulk-equals-loop-of-singles.
    /// </summary>
    public sealed class TemplateConformanceSystemTests
    {
        // ── Fakes ───────────────────────────────────────────────────────────────────

        private sealed class FakeContentDefinitionCatalog : IContentDefinitionCatalog
        {
            private readonly Dictionary<(ContentKind Kind, string BlueprintId), IEntityTemplate> _store = new();

            public List<ContentDefinition> SaveCalls { get; } = new();
            public Func<ContentDefinition, ContentWriteResult>? SaveResultFactory { get; set; }

            public void Seed(ContentKind kind, IEntityTemplate template) => _store[(kind, template.BlueprintId)] = template;

            public IReadOnlyList<ContentSummary> List(ContentKind kind) => throw new NotSupportedException();
            public IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId) => throw new NotSupportedException();

            public ContentDefinition? Load(ContentKind kind, string blueprintId) =>
                _store.TryGetValue((kind, blueprintId), out var template) ? new ContentDefinition(kind, template) : null;

            public Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct = default)
            {
                SaveCalls.Add(definition);
                var result = SaveResultFactory?.Invoke(definition) ?? ContentWriteResult.Ok(definition.BlueprintId);
                if (result.Success)
                    _store[(definition.Kind, definition.BlueprintId)] = definition.Template;
                return Task.FromResult(result);
            }

            public Task<ContentWriteResult> SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<ContentDeleteResult> DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public ContentDefinition CreateNew(ContentKind kind, string name) => throw new NotSupportedException();
        }

        private sealed class FakeBalanceAuditSystem : IBalanceAuditSystem
        {
            public BalanceAuditReport Report { get; set; } =
                new(Array.Empty<BalanceAuditEntry>(), new Dictionary<(int Tier, int Band), int>());

            public BalanceAuditReport Audit() => Report;
        }

        private static (TemplateConformanceSystem System, FakeContentDefinitionCatalog Catalog, FakeBalanceAuditSystem Audit) Build(
            PowerBudgetTunables? tunables = null)
        {
            var catalog = new FakeContentDefinitionCatalog();
            var audit = new FakeBalanceAuditSystem();
            var system = new TemplateConformanceSystem(
                catalog,
                new PowerBudgetSystem(tunables ?? PowerBudgetTunables.Default),
                new ItemPowerProjectionSystem(),
                new MobPowerProjectionSystem(),
                audit);
            return (system, catalog, audit);
        }

        // ── 1. Item fit ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Preview_fits_a_drifted_item_within_the_target_range_preserving_ratio_and_other_fields()
        {
            var (system, catalog, _) = Build();
            var item = new ItemTemplate("item.drifted.test")
            {
                Name = "Drifted Blade",
                Description = "A blade whose bite outgrew its tag.",
                Keywords = { "blade" },
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
                Value = 500,
            };
            catalog.Seed(ContentKind.Item, item);

            var preview = system.Preview(BalanceAuditKind.Item, item.BlueprintId);

            Assert.Equal(ConformanceStatus.Fitted, preview.Status);
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            Assert.Equal(new PowerBand(2, 2), powerBudget.Classify(preview.PowerAfter));

            var atk = Assert.Single(preview.FieldChanges, c => c.Field == ScoreId.AttackPower);
            var def = Assert.Single(preview.FieldChanges, c => c.Field == ScoreId.Defense);
            Assert.Equal(15, atk.Before);
            Assert.Equal(5, def.Before);
            // Ratio preserved exactly here (no correction step needed for this input).
            Assert.Equal(3 * def.After, atk.After);

            // Apply and assert every non-StatBonuses field is byte-identical.
            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);
            Assert.True(applyResult.Success);
            var saved = (ItemTemplate)Assert.Single(catalog.SaveCalls).Template;
            Assert.Equal("Drifted Blade", saved.Name);
            Assert.Equal("A blade whose bite outgrew its tag.", saved.Description);
            Assert.Equal(new[] { "blade" }, saved.Keywords);
            Assert.Equal(2, saved.Tier);
            Assert.Equal(2, saved.Band);
            Assert.Equal(500, saved.Value);
        }

        // ── 2. Mob fit ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task Preview_fits_a_drifted_mob_scaling_attributes_and_pools_with_derived_scores_following_body()
        {
            var (system, catalog, _) = Build();
            var mob = new MobTemplate("mob.drifted.test")
            {
                Name = "Overtagged Goblin",
                Tier = 3,
                Band = 2,
                Body = 12,
                MaxHp = 80,
                SpawnRoomBlueprintId = "room.goblin.den",
                CurrencyLoot = { [CurrencyId.Coin] = (1, 5) },
                Protection = ProtectionFlags.Untargetable,
            };
            catalog.Seed(ContentKind.Mob, mob);

            var preview = system.Preview(BalanceAuditKind.Mob, mob.BlueprintId);

            Assert.Equal(ConformanceStatus.Fitted, preview.Status);
            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            Assert.Equal(new PowerBand(3, 2), powerBudget.Classify(preview.PowerAfter));

            var body = Assert.Single(preview.FieldChanges, c => c.Field == ScoreId.Body);
            var hp = Assert.Single(preview.FieldChanges, c => c.Field == ScoreId.HpMax);
            Assert.Equal(12, body.Before);
            Assert.Equal(80, hp.Before);
            Assert.True(body.After > body.Before, "Body must scale up for a drifted-low mob.");
            Assert.True(hp.After > hp.Before, "MaxHp must scale up for a drifted-low mob.");

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Mob, mob.BlueprintId);
            Assert.True(applyResult.Success);
            var saved = (MobTemplate)Assert.Single(catalog.SaveCalls).Template;

            // Derived AttackPower/Defense follow the scaled Body through the real projection.
            Assert.Equal(saved.Body / 2, new MobPowerProjectionSystem().Project(saved).Scores[ScoreId.AttackPower]);
            Assert.Equal(saved.Body / 4, new MobPowerProjectionSystem().Project(saved).Scores[ScoreId.Defense]);

            // Shop/loot/protection/spawn fields untouched.
            Assert.Equal("room.goblin.den", saved.SpawnRoomBlueprintId);
            Assert.Equal((1, 5), saved.CurrencyLoot[CurrencyId.Coin]);
            Assert.Equal(ProtectionFlags.Untargetable, saved.Protection);
            Assert.Equal(3, saved.Tier);
            Assert.Equal(2, saved.Band);
        }

        // ── 3. Midpoint targeting + determinism golden-number ──────────────────────

        [Fact]
        public void Preview_is_deterministic_across_repeated_calls_over_the_same_disk_state()
        {
            var (system, catalog, _) = Build();
            var item = new ItemTemplate("item.determinism.test")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };
            catalog.Seed(ContentKind.Item, item);

            var first = system.Preview(BalanceAuditKind.Item, item.BlueprintId);
            var second = system.Preview(BalanceAuditKind.Item, item.BlueprintId);

            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.PowerBefore, second.PowerBefore);
            Assert.Equal(first.PowerAfter, second.PowerAfter);
            Assert.Equal(first.CellBefore, second.CellBefore);
            Assert.Equal(first.CellAfter, second.CellAfter);
            Assert.Equal(first.FieldChanges, second.FieldChanges);
        }

        // ── 4. Rounding-correction convergence (and its non-convergent counterpart) ─

        [Fact]
        public void Preview_converges_via_bounded_correction_when_the_scaled_rounding_lands_on_a_tier_boundary()
        {
            // Real Default tunables: Body 12 / MaxHp 80 scaled toward (3,3)'s midpoint rounds to
            // exactly PowerBudgetSystem's BandAnchor(4) — the documented tier-boundary overlap
            // (Classify floors an exact-anchor hit to the *next* tier, band 1) — so the naive
            // single-shot scale lands one integer away from the target cell and needs exactly one
            // bounded ±1 correction step to land on (3,3).
            var (system, catalog, _) = Build();
            var mob = new MobTemplate("mob.boundary.test") { Tier = 3, Band = 3, Body = 12, MaxHp = 80 };
            catalog.Seed(ContentKind.Mob, mob);

            var powerBudget = new PowerBudgetSystem(PowerBudgetTunables.Default);
            var mobProjection = new MobPowerProjectionSystem();

            // Sanity: the naive (uncorrected) single-shot scale really does miss the target cell —
            // proving this test exercises the correction path, not a same-shot convergence.
            var naiveBody = 17;
            var naiveMaxHp = 110;
            var naiveTemplate = new MobTemplate("naive") { Body = naiveBody, MaxHp = naiveMaxHp };
            var naivePower = powerBudget.Estimate(mobProjection.Project(naiveTemplate), 3);
            Assert.NotEqual(new PowerBand(3, 3), powerBudget.Classify(naivePower));

            var preview = system.Preview(BalanceAuditKind.Mob, mob.BlueprintId);

            Assert.Equal(ConformanceStatus.Fitted, preview.Status);
            Assert.Equal(new PowerBand(3, 3), powerBudget.Classify(preview.PowerAfter));
        }

        [Fact]
        public void Preview_returns_NotFittable_RoundingDidNotConverge_for_a_constructed_unreachable_target()
        {
            // A single-field vector whose only weight (125) is wider than the target band (width
            // 41) — the only achievable weighted values are multiples of 125 (0, 125, 250, ...),
            // none of which ever falls inside the target band, so the ±1 correction oscillates
            // between the same two out-of-band values forever and must exhaust the iteration cap.
            var tunables = new PowerBudgetTunables(
                Weights: new Dictionary<ScoreId, int> { [ScoreId.AttackPower] = 125 },
                BandSpan: 1,
                BandsPerTier: 3,
                ReferenceBaseScores: new Dictionary<ScoreId, int> { [ScoreId.AttackPower] = 0 },
                MaxTier: 0,
                TierBaselineStep: 1,
                TrackedScores: new[] { ScoreId.AttackPower });
            var (system, catalog, _) = Build(tunables);

            var item = new ItemTemplate("item.unreachable.test")
            {
                Tier = 0,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 1) },
            };
            catalog.Seed(ContentKind.Item, item);

            var preview = system.Preview(BalanceAuditKind.Item, item.BlueprintId);

            Assert.Equal(ConformanceStatus.NotFittable, preview.Status);
            Assert.Equal(ConformanceNotFittableReason.RoundingDidNotConverge, preview.NotFittableReason);
            Assert.Empty(catalog.SaveCalls);
        }

        // ── 5. AlreadyInRange ───────────────────────────────────────────────────────

        [Fact]
        public async Task AlreadyInRange_template_yields_no_field_changes_and_zero_SaveAsync_calls()
        {
            var (system, catalog, _) = Build();
            // Already classifies as (2, 2) under Default tunables (see the item-fit test above —
            // this is that test's fitted output, seeded directly).
            var item = new ItemTemplate("item.already-in-range.test")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 42), new EquipmentStatBonus(ScoreId.Defense, 14) },
            };
            catalog.Seed(ContentKind.Item, item);

            var preview = system.Preview(BalanceAuditKind.Item, item.BlueprintId);
            Assert.Equal(ConformanceStatus.AlreadyInRange, preview.Status);
            Assert.Empty(preview.FieldChanges);
            Assert.Equal(preview.PowerBefore, preview.PowerAfter);

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);
            Assert.Equal(ConformanceStatus.AlreadyInRange, applyResult.Status);
            Assert.True(applyResult.Success);
            Assert.Empty(catalog.SaveCalls);
        }

        // ── 6. NotFittable guards ───────────────────────────────────────────────────

        [Fact]
        public async Task NotFittable_for_a_zero_weighted_power_vector_with_no_write()
        {
            var (system, catalog, _) = Build();
            // ManaMax carries a zero weight in PowerBudgetTunables.Default — the whole vector
            // contributes zero power, so there is nothing to scale.
            var item = new ItemTemplate("item.zero-vector.test")
            {
                Tier = 1,
                Band = 1,
                StatBonuses = { new EquipmentStatBonus(ScoreId.ManaMax, 500) },
            };
            catalog.Seed(ContentKind.Item, item);

            var preview = system.Preview(BalanceAuditKind.Item, item.BlueprintId);
            Assert.Equal(ConformanceStatus.NotFittable, preview.Status);
            Assert.Equal(ConformanceNotFittableReason.ZeroWeightedPowerVector, preview.NotFittableReason);

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);
            Assert.False(applyResult.Success);
            Assert.Empty(catalog.SaveCalls);
        }

        [Fact]
        public async Task NotFittable_for_an_unbanded_Band_zero_template_with_no_write()
        {
            var (system, catalog, _) = Build();
            var item = new ItemTemplate("item.unbanded.test")
            {
                Tier = 2,
                Band = 0,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15) },
            };
            catalog.Seed(ContentKind.Item, item);

            var preview = system.Preview(BalanceAuditKind.Item, item.BlueprintId);
            Assert.Equal(ConformanceStatus.NotFittable, preview.Status);
            Assert.Equal(ConformanceNotFittableReason.UnbandedTemplate, preview.NotFittableReason);

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);
            Assert.False(applyResult.Success);
            Assert.Empty(catalog.SaveCalls);
        }

        // ── 7. Apply re-derives from disk ───────────────────────────────────────────

        [Fact]
        public async Task ApplyAsync_re_derives_the_fit_from_disk_never_trusting_a_prior_preview()
        {
            var (system, catalog, _) = Build();
            var original = new ItemTemplate("item.redive.test")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };
            catalog.Seed(ContentKind.Item, original);

            var stalePreview = system.Preview(BalanceAuditKind.Item, original.BlueprintId);
            Assert.Equal(ConformanceStatus.Fitted, stalePreview.Status);

            // A designer edits the template on disk after the preview was taken — a different
            // ratio (1:2 instead of the original 3:1) so the two fits are trivially distinguishable.
            var edited = new ItemTemplate("item.redive.test")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 10), new EquipmentStatBonus(ScoreId.Defense, 20) },
            };
            catalog.Seed(ContentKind.Item, edited);

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, original.BlueprintId);

            Assert.True(applyResult.Success);
            var saved = (ItemTemplate)Assert.Single(catalog.SaveCalls).Template;
            var atk = saved.StatBonuses.Single(b => b.TargetScore == ScoreId.AttackPower).Magnitude;
            var def = saved.StatBonuses.Single(b => b.TargetScore == ScoreId.Defense).Magnitude;

            // The saved ratio reflects the edited (disk-current) template, not the stale preview's
            // 3:1 — Defense must outweigh AttackPower here (the edited template's 1:2 skew).
            Assert.True(def > atk, $"Expected the disk-derived (1:2) ratio, got AttackPower={atk}, Defense={def}.");
        }

        // ── 8. Validation refusal propagation ───────────────────────────────────────

        [Fact]
        public async Task ApplyAsync_surfaces_a_catalog_validation_refusal_with_exactly_one_attempted_write()
        {
            var (system, catalog, _) = Build();
            var item = new ItemTemplate("item.refused.test")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };
            catalog.Seed(ContentKind.Item, item);
            catalog.SaveResultFactory = def => ContentWriteResult.Failed(def.BlueprintId, new[] { "forced validation failure" });

            var applyResult = await system.ApplyAsync(BalanceAuditKind.Item, item.BlueprintId);

            Assert.False(applyResult.Success);
            Assert.Equal(ConformanceStatus.WriteRefused, applyResult.Status);
            Assert.Contains("forced validation failure", applyResult.Errors);
            Assert.Single(catalog.SaveCalls);
        }

        // ── 9. Bulk = loop of singles (INV-19) ──────────────────────────────────────

        [Fact]
        public async Task ApplyFlaggedAsync_loops_ApplyAsync_over_the_audit_flagged_set()
        {
            var (system, catalog, audit) = Build();

            var fittableA = new ItemTemplate("item.bulk.a")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            };
            var notFittableB = new ItemTemplate("item.bulk.b") { Tier = 2, Band = 0 };
            var fittableC = new ItemTemplate("item.bulk.c")
            {
                Tier = 2,
                Band = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 30), new EquipmentStatBonus(ScoreId.Defense, 10) },
            };
            catalog.Seed(ContentKind.Item, fittableA);
            catalog.Seed(ContentKind.Item, notFittableB);
            catalog.Seed(ContentKind.Item, fittableC);

            audit.Report = new BalanceAuditReport(
                new[]
                {
                    new BalanceAuditEntry(BalanceAuditKind.Item, "item.bulk.a", 2, 2, 0, 0, 99),
                    new BalanceAuditEntry(BalanceAuditKind.Item, "item.bulk.b", 2, 0, 0, 0, 99),
                    new BalanceAuditEntry(BalanceAuditKind.Item, "item.bulk.c", 2, 2, 0, 0, 99),
                },
                new Dictionary<(int Tier, int Band), int>());

            var bulkResult = await system.ApplyFlaggedAsync();

            Assert.Equal(3, bulkResult.Results.Count);
            Assert.Equal(2, catalog.SaveCalls.Count);
            Assert.Equal("item.bulk.a", bulkResult.Results[0].BlueprintId);
            Assert.True(bulkResult.Results[0].Success);
            Assert.Equal("item.bulk.b", bulkResult.Results[1].BlueprintId);
            Assert.Equal(ConformanceStatus.NotFittable, bulkResult.Results[1].Status);
            Assert.False(bulkResult.Results[1].Success);
            Assert.Equal("item.bulk.c", bulkResult.Results[2].BlueprintId);
            Assert.True(bulkResult.Results[2].Success);

            // PreviewFlagged is not a bulk-path fork — it returns exactly what calling Preview
            // individually over the same flagged set would.
            var previewFlagged = system.PreviewFlagged();
            Assert.Equal(3, previewFlagged.Count);
            foreach (var entry in audit.Report.Drifted)
            {
                var expected = system.Preview(entry.Kind, entry.BlueprintId);
                var actual = previewFlagged.Single(p => p.BlueprintId == entry.BlueprintId);
                Assert.Equal(expected.Status, actual.Status);
                Assert.Equal(expected.PowerAfter, actual.PowerAfter);
            }
        }
    }
}
