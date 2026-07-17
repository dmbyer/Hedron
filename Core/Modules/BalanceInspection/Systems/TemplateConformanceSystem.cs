using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    /// <summary>
    /// Default <see cref="ITemplateConformanceSystem"/>. The oracle (<see cref="IPowerBudgetSystem"/>)
    /// is the only source of target math — this system exploits its documented weighted-sum
    /// linearity to compute a closed-form scale factor, then verifies with the real projection
    /// seams and corrects, so a future oracle change degrades to more correction steps, never to
    /// silent drift (see docs/implementation-plans/conformance-tooling.md Design notes).
    /// </summary>
    public sealed class TemplateConformanceSystem : ITemplateConformanceSystem
    {
        private const string ScratchBlueprintId = "~conformance-scratch~";
        private const int MaxCorrectionIterations = 8;

        // Exactly the fields IMobPowerProjectionSystem reads (mirrors the item side's StatBonuses).
        private static readonly ScoreId[] MobKnobs =
        {
            ScoreId.Mind, ScoreId.Body, ScoreId.Spirit, ScoreId.Attunement,
            ScoreId.HpMax, ScoreId.ManaMax, ScoreId.StaminaMax, ScoreId.AstraMax,
        };

        private static readonly PowerSnapshot EmptySnapshot = new(new Dictionary<ScoreId, int>());

        private readonly IContentDefinitionCatalog _catalog;
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IItemPowerProjectionSystem _itemProjection;
        private readonly IMobPowerProjectionSystem _mobProjection;
        private readonly IBalanceAuditSystem _audit;

        public TemplateConformanceSystem(
            IContentDefinitionCatalog catalog,
            IPowerBudgetSystem powerBudget,
            IItemPowerProjectionSystem itemProjection,
            IMobPowerProjectionSystem mobProjection,
            IBalanceAuditSystem audit)
        {
            _catalog = catalog;
            _powerBudget = powerBudget;
            _itemProjection = itemProjection;
            _mobProjection = mobProjection;
            _audit = audit;
        }

        public ConformancePreview Preview(BalanceAuditKind kind, string blueprintId) =>
            Fit(kind, blueprintId).ToPreview();

        public IReadOnlyList<ConformancePreview> PreviewFlagged()
        {
            var previews = new List<ConformancePreview>();
            foreach (var entry in _audit.Audit().Drifted)
                previews.Add(Preview(entry.Kind, entry.BlueprintId));
            return previews;
        }

        public async Task<ConformanceApplyResult> ApplyAsync(
            BalanceAuditKind kind, string blueprintId, CancellationToken ct = default)
        {
            // Re-derives from disk every call — never trusts a prior Preview (idempotent apply).
            var outcome = Fit(kind, blueprintId);

            if (outcome.Status == ConformanceStatus.AlreadyInRange)
                return ConformanceApplyResult.AlreadyInRange(kind, blueprintId);

            if (outcome.Status == ConformanceStatus.NotFittable)
                return ConformanceApplyResult.NotFittable(kind, blueprintId, outcome.Reason);

            var writeResult = await _catalog.SaveAsync(outcome.FittedDefinition!, ct).ConfigureAwait(false);

            return writeResult.Success
                ? ConformanceApplyResult.Fitted(kind, blueprintId, writeResult.Warnings)
                : ConformanceApplyResult.Failed(kind, blueprintId, writeResult.Errors);
        }

        public async Task<ConformanceBulkResult> ApplyFlaggedAsync(CancellationToken ct = default)
        {
            var results = new List<ConformanceApplyResult>();
            foreach (var entry in _audit.Audit().Drifted)
                results.Add(await ApplyAsync(entry.Kind, entry.BlueprintId, ct).ConfigureAwait(false));
            return new ConformanceBulkResult(results);
        }

        // ── Fit dispatch (mirrors BalanceAuditSystem's item/mob switch) ───────────────

        private FitOutcome Fit(BalanceAuditKind kind, string blueprintId)
        {
            var contentKind = kind == BalanceAuditKind.Item ? ContentKind.Item : ContentKind.Mob;
            var definition = _catalog.Load(contentKind, blueprintId)
                ?? throw new InvalidOperationException(
                    $"TemplateConformanceSystem: no {contentKind} definition found on disk for blueprint id '{blueprintId}'.");

            return kind == BalanceAuditKind.Item
                ? FitItem(blueprintId, (ItemTemplate)definition.Template)
                : FitMob(blueprintId, (MobTemplate)definition.Template);
        }

        private FitOutcome FitItem(string blueprintId, ItemTemplate template)
        {
            var powerBefore = _powerBudget.Estimate(_itemProjection.Project(template), template.Tier);
            var cellBefore = _powerBudget.Classify(powerBefore);

            if (template.Band == 0)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Item, blueprintId, ConformanceNotFittableReason.UnbandedTemplate, powerBefore, cellBefore);

            var targetRange = _powerBudget.TargetRange(template.Tier, template.Band);
            if (OnTarget(cellBefore, template.Tier, template.Band))
                return FitOutcome.AlreadyInRange(BalanceAuditKind.Item, blueprintId, powerBefore, cellBefore);

            var originalFields = template.StatBonuses
                .Select(b => new FieldValue(b.TargetScore, b.Magnitude))
                .ToArray();

            var variablePower = _powerBudget.Estimate(ProjectItemFields(originalFields), tier: 0);
            if (variablePower == 0)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Item, blueprintId, ConformanceNotFittableReason.ZeroWeightedPowerVector, powerBefore, cellBefore);

            var scaledFields = ScaleFields(originalFields, template.Tier, targetRange, variablePower);
            var converged = Converge(scaledFields, template.Tier, template.Band, targetRange, ProjectItemFields);
            if (!converged.Converged)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Item, blueprintId, ConformanceNotFittableReason.RoundingDidNotConverge, powerBefore, cellBefore);

            var changes = BuildFieldChanges(originalFields, converged.Fields);
            var fittedTemplate = CloneItem(template, converged.Fields);

            return FitOutcome.Fitted(
                BalanceAuditKind.Item, blueprintId, powerBefore, converged.Power, cellBefore, converged.Cell,
                changes, new ContentDefinition(ContentKind.Item, fittedTemplate));
        }

        private FitOutcome FitMob(string blueprintId, MobTemplate template)
        {
            var powerBefore = _powerBudget.Estimate(_mobProjection.Project(template), template.Tier);
            var cellBefore = _powerBudget.Classify(powerBefore);

            if (template.Band == 0)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Mob, blueprintId, ConformanceNotFittableReason.UnbandedTemplate, powerBefore, cellBefore);

            var targetRange = _powerBudget.TargetRange(template.Tier, template.Band);
            if (OnTarget(cellBefore, template.Tier, template.Band))
                return FitOutcome.AlreadyInRange(BalanceAuditKind.Mob, blueprintId, powerBefore, cellBefore);

            var originalFields = MobKnobs
                .Select(s => new FieldValue(s, MobFieldValue(template, s)))
                .ToArray();

            var variablePower = _powerBudget.Estimate(ProjectMobFields(originalFields), tier: 0);
            if (variablePower == 0)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Mob, blueprintId, ConformanceNotFittableReason.ZeroWeightedPowerVector, powerBefore, cellBefore);

            var scaledFields = ScaleFields(originalFields, template.Tier, targetRange, variablePower);
            var converged = Converge(scaledFields, template.Tier, template.Band, targetRange, ProjectMobFields);
            if (!converged.Converged)
                return FitOutcome.NotFittable(
                    BalanceAuditKind.Mob, blueprintId, ConformanceNotFittableReason.RoundingDidNotConverge, powerBefore, cellBefore);

            var changes = BuildFieldChanges(originalFields, converged.Fields);
            var fittedTemplate = CloneMob(template, converged.Fields);

            return FitOutcome.Fitted(
                BalanceAuditKind.Mob, blueprintId, powerBefore, converged.Power, cellBefore, converged.Cell,
                changes, new ContentDefinition(ContentKind.Mob, fittedTemplate));
        }

        // ── Shared scale / verify / correct math ───────────────────────────────────

        /// <summary>
        /// Closed-form uniform scale toward the target cell's midpoint:
        /// <c>k = (targetMid - tierTerm) / variablePower</c>, rounded half-away-from-zero per field.
        /// </summary>
        private FieldValue[] ScaleFields(FieldValue[] fields, int tier, PowerRange targetRange, int variablePower)
        {
            var tierTerm = _powerBudget.Estimate(EmptySnapshot, tier);
            var targetMid = Midpoint(targetRange);
            var k = (targetMid - tierTerm) / (double)variablePower;

            return fields
                .Select(f => f with { Value = RoundHalfAwayFromZero(f.Value * k) })
                .ToArray();
        }

        /// <summary>
        /// Verifies the scaled fields via the real projection seam; if the classified cell isn't
        /// the target (Tier, Band), nudges the field with the largest measured per-unit power
        /// contribution by ±1 toward the target and re-verifies, up to
        /// <see cref="MaxCorrectionIterations"/> times.
        /// </summary>
        private ConvergeResult Converge(
            FieldValue[] scaledFields, int tier, int band, PowerRange targetRange, Func<FieldValue[], PowerSnapshot> project)
        {
            var fields = scaledFields;
            var power = _powerBudget.Estimate(project(fields), tier);
            var cell = _powerBudget.Classify(power);

            for (var i = 0; i <= MaxCorrectionIterations; i++)
            {
                if (OnTarget(cell, tier, band))
                    return new ConvergeResult(true, fields, power, cell);
                if (i == MaxCorrectionIterations)
                    break;

                var nudgeField = PickLargestContributionField(fields, tier, project, power);
                var direction = power < targetRange.MinPower ? 1 : -1;
                fields = fields
                    .Select(f => f.Field == nudgeField ? f with { Value = f.Value + direction } : f)
                    .ToArray();

                power = _powerBudget.Estimate(project(fields), tier);
                cell = _powerBudget.Classify(power);
            }

            return new ConvergeResult(false, fields, power, cell);
        }

        /// <summary>
        /// Probes each field's real +1 marginal power effect (captures derived fields like a mob's
        /// Body-driven AttackPower/Defense) and returns the field with the largest swing. First
        /// field wins ties — deterministic (INV-26), no ambient ordering dependency.
        /// </summary>
        private ScoreId PickLargestContributionField(
            FieldValue[] fields, int tier, Func<FieldValue[], PowerSnapshot> project, int basePower)
        {
            var bestField = fields[0].Field;
            var bestDelta = -1;

            foreach (var field in fields)
            {
                var probe = fields
                    .Select(f => f.Field == field.Field ? f with { Value = f.Value + 1 } : f)
                    .ToArray();
                var delta = Math.Abs(_powerBudget.Estimate(project(probe), tier) - basePower);
                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    bestField = field.Field;
                }
            }

            return bestField;
        }

        private PowerSnapshot ProjectItemFields(FieldValue[] fields)
        {
            var bonuses = fields.Select(f => new EquipmentStatBonus(f.Field, f.Value)).ToList();
            return _itemProjection.Project(new ItemTemplate(ScratchBlueprintId) { StatBonuses = bonuses });
        }

        private PowerSnapshot ProjectMobFields(FieldValue[] fields)
        {
            var scratch = new MobTemplate(ScratchBlueprintId);
            foreach (var f in fields)
                SetMobField(scratch, f.Field, f.Value);
            return _mobProjection.Project(scratch);
        }

        // ── Field <-> template plumbing ─────────────────────────────────────────────

        private readonly record struct FieldValue(ScoreId Field, int Value);

        private readonly record struct ConvergeResult(
            bool Converged, FieldValue[] Fields, int Power, PowerBand Cell);

        private static int MobFieldValue(MobTemplate template, ScoreId field) => field switch
        {
            ScoreId.Mind => template.Mind,
            ScoreId.Body => template.Body,
            ScoreId.Spirit => template.Spirit,
            ScoreId.Attunement => template.Attunement,
            ScoreId.HpMax => template.MaxHp,
            ScoreId.ManaMax => template.MaxMana,
            ScoreId.StaminaMax => template.MaxStamina,
            ScoreId.AstraMax => template.MaxAstra,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a mob conformance knob."),
        };

        private static void SetMobField(MobTemplate template, ScoreId field, int value)
        {
            switch (field)
            {
                case ScoreId.Mind: template.Mind = value; break;
                case ScoreId.Body: template.Body = value; break;
                case ScoreId.Spirit: template.Spirit = value; break;
                case ScoreId.Attunement: template.Attunement = value; break;
                case ScoreId.HpMax: template.MaxHp = value; break;
                case ScoreId.ManaMax: template.MaxMana = value; break;
                case ScoreId.StaminaMax: template.MaxStamina = value; break;
                case ScoreId.AstraMax: template.MaxAstra = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(field), field, "Not a mob conformance knob.");
            }
        }

        private static ItemTemplate CloneItem(ItemTemplate source, FieldValue[] fields)
        {
            var bonuses = fields.Select(f => new EquipmentStatBonus(f.Field, f.Value)).ToList();
            return new ItemTemplate(source.BlueprintId)
            {
                Name = source.Name,
                Description = source.Description,
                Keywords = new List<string>(source.Keywords),
                ItemType = source.ItemType,
                WornSlots = new List<WornSlot>(source.WornSlots),
                SpawnRoomBlueprintId = source.SpawnRoomBlueprintId,
                StatBonuses = bonuses,
                Value = source.Value,
                Tier = source.Tier,
                Band = source.Band,
            };
        }

        private static MobTemplate CloneMob(MobTemplate source, FieldValue[] fields)
        {
            var clone = new MobTemplate(source.BlueprintId)
            {
                Name = source.Name,
                Description = source.Description,
                Keywords = new List<string>(source.Keywords),
                MobType = source.MobType,
                SpawnRoomBlueprintId = source.SpawnRoomBlueprintId,
                Level = source.Level,
                Mind = source.Mind,
                Body = source.Body,
                Spirit = source.Spirit,
                Attunement = source.Attunement,
                MaxHp = source.MaxHp,
                MaxMana = source.MaxMana,
                MaxStamina = source.MaxStamina,
                MaxAstra = source.MaxAstra,
                CurrencyLoot = new Dictionary<Economy.CurrencyId, (int Min, int Max)>(source.CurrencyLoot),
                Protection = source.Protection,
                Tier = source.Tier,
                Band = source.Band,
                IsShop = source.IsShop,
                ShopAcceptedCurrency = source.ShopAcceptedCurrency,
                ShopTillSeed = source.ShopTillSeed,
                ShopRatioOverride = source.ShopRatioOverride,
                ShopBaseStock = new List<ShopStockRow>(source.ShopBaseStock),
            };
            foreach (var f in fields)
                SetMobField(clone, f.Field, f.Value);
            return clone;
        }

        private static IReadOnlyList<ConformanceFieldChange> BuildFieldChanges(FieldValue[] before, FieldValue[] after)
        {
            var changes = new List<ConformanceFieldChange>(before.Length);
            for (var i = 0; i < before.Length; i++)
                changes.Add(new ConformanceFieldChange(before[i].Field, before[i].Value, after[i].Value));
            return changes;
        }

        private static int Midpoint(PowerRange range) => (range.MinPower + range.MaxPower) / 2;

        // "Classifies in range" (Main flow step 3) means the classified cell equals the authored
        // target cell — the same zero-drift definition IBalanceAuditSystem uses — not raw
        // PowerRange containment, which disagrees with Classify in the below-every-anchor
        // tier-0 fallback (see IPowerBudgetSystem.Classify).
        private static bool OnTarget(PowerBand cell, int tier, int band) => cell.Tier == tier && cell.Band == band;

        private static int RoundHalfAwayFromZero(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        // ── Internal fit-outcome carrier (Preview and ApplyAsync share this shape) ──

        private sealed record FitOutcome(
            BalanceAuditKind Kind,
            string BlueprintId,
            ConformanceStatus Status,
            ConformanceNotFittableReason Reason,
            int PowerBefore,
            int PowerAfter,
            PowerBand CellBefore,
            PowerBand CellAfter,
            IReadOnlyList<ConformanceFieldChange> FieldChanges,
            ContentDefinition? FittedDefinition)
        {
            public static FitOutcome AlreadyInRange(BalanceAuditKind kind, string blueprintId, int power, PowerBand cell) =>
                new(kind, blueprintId, ConformanceStatus.AlreadyInRange, ConformanceNotFittableReason.None,
                    power, power, cell, cell, Array.Empty<ConformanceFieldChange>(), null);

            public static FitOutcome NotFittable(
                BalanceAuditKind kind, string blueprintId, ConformanceNotFittableReason reason, int power, PowerBand cell) =>
                new(kind, blueprintId, ConformanceStatus.NotFittable, reason,
                    power, power, cell, cell, Array.Empty<ConformanceFieldChange>(), null);

            public static FitOutcome Fitted(
                BalanceAuditKind kind, string blueprintId, int powerBefore, int powerAfter,
                PowerBand cellBefore, PowerBand cellAfter, IReadOnlyList<ConformanceFieldChange> changes,
                ContentDefinition fittedDefinition) =>
                new(kind, blueprintId, ConformanceStatus.Fitted, ConformanceNotFittableReason.None,
                    powerBefore, powerAfter, cellBefore, cellAfter, changes, fittedDefinition);

            public ConformancePreview ToPreview() =>
                new(Kind, BlueprintId, Status, Reason, PowerBefore, PowerAfter, CellBefore, CellAfter, FieldChanges);
        }
    }
}
