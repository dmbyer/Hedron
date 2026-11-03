using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Systems;

namespace Hedron.Tests.World
{
    /// <summary>
    /// The dev-world baseline content policy (Phase 5 content drops, D20) as named, pure predicates
    /// over loaded templates. Each returns its violations (empty = conforming). Policy, not an
    /// engine rule: it describes what dev-world promises, so it lives beside the dev-world fixture
    /// tests. Everything is derived from content — hunting areas from what they place, the band
    /// list from designations — so later drops pass without editing the rules. "In designation" and
    /// "within drift" reuse the engine's own judgements (<see cref="AreaDesignation.Contains"/>,
    /// <see cref="IBalanceAuditSystem"/>); nothing here computes power.
    /// </summary>
    internal static class DevWorldConformanceRules
    {
        /// <summary>A mob placed by a room spawn rule, with the room and area that place it.</summary>
        internal readonly record struct HuntingPlacement(string RoomBlueprintId, string? AreaBlueprintId, MobTemplate Mob);

        /// <summary>
        /// Every placement of a <em>hunting mob</em>: a mob template placed by any room spawn rule
        /// whose protection lacks <see cref="ProtectionFlags.Untargetable"/> and which is not a shop.
        /// </summary>
        internal static IReadOnlyList<HuntingPlacement> HuntingPlacements(ITemplateRegistry registry, IAreaPlacementSource placement) =>
            placement.Sites()
                .Where(site => placement.TagOf(site.Rule.BlueprintId) is { Kind: PlacedKind.Mob })
                .Select(site => (site, mob: registry.TryGet(site.Rule.BlueprintId, out var t) ? t as MobTemplate : null))
                .Where(x => x.mob is not null && IsHunting(x.mob))
                .Select(x => new HuntingPlacement(x.site.RoomBlueprintId, x.site.AreaBlueprintId, x.mob!))
                .ToList();

        private static bool IsHunting(MobTemplate mob) =>
            !mob.Protection.HasFlag(ProtectionFlags.Untargetable) && !mob.IsShop;

        /// <summary>The distinct hunting mobs (by blueprint id) placed anywhere.</summary>
        internal static IReadOnlyList<MobTemplate> HuntingMobs(ITemplateRegistry registry, IAreaPlacementSource placement) =>
            HuntingPlacements(registry, placement)
                .Select(p => p.Mob)
                .DistinctBy(m => m.BlueprintId, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Each designated hunting area with its designation.</summary>
        internal static IReadOnlyList<(string AreaBlueprintId, AreaDesignation Designation)> DesignatedHuntingAreas(
            ITemplateRegistry registry, IAreaPlacementSource placement) =>
            HuntingPlacements(registry, placement)
                .Select(p => p.AreaBlueprintId)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(area => (area, designation: placement.DesignationOf(area)))
                .Where(x => x.designation is not null)
                .Select(x => (x.area, x.designation!))
                .OrderBy(x => x.area, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// The band list: the distinct (Tier, Band) cells covered by the designated hunting areas.
        /// </summary>
        internal static IReadOnlyList<PowerBand> BandList(
            ITemplateRegistry registry, IAreaPlacementSource placement, PowerBudgetTunables tunables) =>
            DesignatedHuntingAreas(registry, placement)
                .SelectMany(x => Cells(x.Designation, tunables))
                .Distinct()
                .OrderBy(c => tunables.GlobalBandIndex(c.Tier, c.Band))
                .ToList();

        private static IEnumerable<PowerBand> Cells(AreaDesignation designation, PowerBudgetTunables tunables)
        {
            for (var tier = designation.Min.Tier; tier <= designation.Max.Tier; tier++)
                for (var band = 1; band <= tunables.BandsPerTier; band++)
                    if (designation.Contains(new PowerBand(tier, band)))
                        yield return new PowerBand(tier, band);
        }

        // ── Rules ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every hunting area is designated. A hunting mob in a room with no area is a violation too,
        /// so "the hub places no targetable mob" is enforced, not just stated.
        /// </summary>
        internal static IReadOnlyList<string> EveryHuntingAreaIsDesignated(ITemplateRegistry registry, IAreaPlacementSource placement)
        {
            var violations = new List<string>();
            foreach (var p in HuntingPlacements(registry, placement))
            {
                if (p.AreaBlueprintId is null)
                    violations.Add($"Room '{p.RoomBlueprintId}' has no area but places hunting mob '{p.Mob.BlueprintId}'.");
                else if (placement.DesignationOf(p.AreaBlueprintId) is null)
                    violations.Add($"Area '{p.AreaBlueprintId}' places hunting mob '{p.Mob.BlueprintId}' (room '{p.RoomBlueprintId}') but is undesignated.");
            }
            return violations.Distinct().ToList();
        }

        /// <summary>
        /// Every hunting mob is banded (<c>Band ≥ 1</c>). Both engine judgements skip <c>Band == 0</c>,
        /// so an unbanded mob in a designated area would otherwise pass every rule.
        /// </summary>
        internal static IReadOnlyList<string> EveryHuntingMobIsBanded(ITemplateRegistry registry, IAreaPlacementSource placement) =>
            HuntingMobs(registry, placement)
                .Where(m => m.Band < 1)
                .Select(m => $"Hunting mob '{m.BlueprintId}' is unbanded (Tier {m.Tier}, Band {m.Band}).")
                .ToList();

        /// <summary>Non-vacuity: at least one designated hunting area exists, so per-band rules bite.</summary>
        internal static IReadOnlyList<string> AtLeastOneDesignatedHuntingArea(
            ITemplateRegistry registry, IAreaPlacementSource placement, PowerBudgetTunables tunables) =>
            BandList(registry, placement, tunables).Count > 0
                ? Array.Empty<string>()
                : new[] { "No designated hunting area: the band list is empty, so every per-band rule passes vacuously." };

        /// <summary>
        /// Each designated hunting area places ≥3 distinct hunting mobs whose authored cell its
        /// designation contains (the same containment the out-of-designation check uses).
        /// </summary>
        internal static IReadOnlyList<string> EachDesignatedHuntingAreaHasThreeMobsInCell(
            ITemplateRegistry registry, IAreaPlacementSource placement)
        {
            var placements = HuntingPlacements(registry, placement);
            var violations = new List<string>();
            foreach (var (area, designation) in DesignatedHuntingAreas(registry, placement))
            {
                var inCell = placements
                    .Where(p => string.Equals(p.AreaBlueprintId, area, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Mob)
                    .Where(m => m.Band >= 1 && designation.Contains(new PowerBand(m.Tier, m.Band)))
                    .Select(m => m.BlueprintId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (inCell < 3)
                    violations.Add($"Area '{area}' ({designation}) places {inCell} distinct hunting mob(s) in its designation; needs ≥3.");
            }
            return violations;
        }

        /// <summary>Every hunting mob has a coin loot range with <c>Max &gt; 0</c> and <c>Min ≤ Max</c>.</summary>
        internal static IReadOnlyList<string> EveryHuntingMobHasCoinLoot(ITemplateRegistry registry, IAreaPlacementSource placement)
        {
            var violations = new List<string>();
            foreach (var mob in HuntingMobs(registry, placement))
            {
                if (!mob.CurrencyLoot.TryGetValue(CurrencyId.Coin, out var range))
                    violations.Add($"Hunting mob '{mob.BlueprintId}' has no coin loot.");
                else if (range.Max <= 0 || range.Min > range.Max)
                    violations.Add($"Hunting mob '{mob.BlueprintId}' has an invalid coin loot range {range.Min}-{range.Max}.");
            }
            return violations;
        }

        /// <summary>
        /// The mean coin midpoint <c>(Min+Max)/2</c> of hunting mobs strictly increases from band to
        /// band, ordered by <see cref="PowerBudgetTunables.GlobalBandIndex"/>. Trivial with one band.
        /// </summary>
        internal static IReadOnlyList<string> CoinLootRisesByBand(
            ITemplateRegistry registry, IAreaPlacementSource placement, PowerBudgetTunables tunables)
        {
            var byBand = HuntingMobs(registry, placement)
                .Where(m => m.Band >= 1)
                .GroupBy(m => new PowerBand(m.Tier, m.Band))
                .Select(g => (Cell: g.Key, Mean: g.Average(Midpoint)))
                .OrderBy(x => tunables.GlobalBandIndex(x.Cell.Tier, x.Cell.Band))
                .ToList();

            var violations = new List<string>();
            for (var i = 1; i < byBand.Count; i++)
            {
                var (lower, higher) = (byBand[i - 1], byBand[i]);
                if (higher.Mean <= lower.Mean)
                    violations.Add(
                        $"Mean coin midpoint does not rise: Tier {higher.Cell.Tier} Band {higher.Cell.Band} ({higher.Mean:0.##}) " +
                        $"≤ Tier {lower.Cell.Tier} Band {lower.Cell.Band} ({lower.Mean:0.##}).");
            }
            return violations;
        }

        private static double Midpoint(MobTemplate mob) =>
            mob.CurrencyLoot.TryGetValue(CurrencyId.Coin, out var r) ? (r.Min + r.Max) / 2.0 : 0;

        /// <summary>
        /// Every found item (any non-mob blueprint placed by a room spawn rule) and every shop item
        /// (any blueprint in any mob's base stock) resolves to an item template with <c>Value &gt; 0</c>.
        /// </summary>
        internal static IReadOnlyList<string> EveryFoundAndShopItemHasValue(ITemplateRegistry registry, IAreaPlacementSource placement)
        {
            var violations = new List<string>();

            foreach (var site in placement.Sites())
            {
                if (placement.TagOf(site.Rule.BlueprintId) is { Kind: PlacedKind.Mob })
                    continue;
                Check(site.Rule.BlueprintId, $"found item (room '{site.RoomBlueprintId}' rule '{site.Rule.Id}')");
            }

            foreach (var mob in AllOf<MobTemplate>(registry))
                foreach (var row in mob.ShopBaseStock)
                    Check(row.BlueprintId, $"shop item (mob '{mob.BlueprintId}')");

            return violations.Distinct().ToList();

            void Check(string blueprintId, string role)
            {
                if (!registry.TryGet(blueprintId, out var t) || t is not ItemTemplate item)
                    violations.Add($"{role} '{blueprintId}' is not a known item template.");
                else if (item.Value <= 0)
                    violations.Add($"{role} '{blueprintId}' has Value {item.Value}; must be > 0.");
            }
        }

        /// <summary>
        /// Every item template that carries power (non-empty stat bonuses) is banded. Junk stays
        /// untagged by having no bonuses; the drift audit skips <c>Band == 0</c>.
        /// </summary>
        internal static IReadOnlyList<string> EveryPoweredItemIsBanded(ITemplateRegistry registry) =>
            AllOf<ItemTemplate>(registry)
                .Where(i => i.StatBonuses.Count > 0 && i.Band < 1)
                .Select(i => $"Item '{i.BlueprintId}' carries stat bonuses but is unbanded (Tier {i.Tier}, Band {i.Band}).")
                .ToList();

        /// <summary>The band-drift audit reports nothing past tolerance (the audit's own judgement, reused).</summary>
        internal static IReadOnlyList<string> BandDriftAuditIsClean(IBalanceAuditSystem audit) =>
            audit.Audit().Drifted
                .Select(d => $"{d.Kind} '{d.BlueprintId}' authored Tier {d.AuthoredTier} Band {d.AuthoredBand} " +
                             $"computes to Tier {d.ComputedTier} Band {d.ComputedBand} (drift {d.Drift}).")
                .ToList();

        /// <summary>
        /// Every committed scenario under <paramref name="scenarioDirectory"/> loads, and every
        /// <see cref="CombatantSourceKind.MobTemplate"/> combatant names a mob template in the registry.
        /// </summary>
        internal static IReadOnlyList<string> EveryCommittedScenarioResolves(
            ITemplateRegistry registry, ISimScenarioStore store, string scenarioDirectory)
        {
            var violations = new List<string>();
            if (!Directory.Exists(scenarioDirectory))
                return violations;

            foreach (var path in Directory.GetFiles(scenarioDirectory, "*.yaml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetRelativePath(scenarioDirectory, path);
                ScenarioDefinition scenario;
                try
                {
                    scenario = store.Load(path);
                }
                catch (Exception ex)
                {
                    violations.Add($"Scenario '{name}' does not load: {ex.Message}");
                    continue;
                }

                foreach (var combatant in scenario.Sides.SelectMany(s => s.Combatants))
                {
                    if (combatant.Source != CombatantSourceKind.MobTemplate)
                        continue;
                    if (!registry.TryGet(combatant.MobBlueprintId ?? string.Empty, out var t) || t is not MobTemplate)
                        violations.Add($"Scenario '{name}' names mob '{combatant.MobBlueprintId}', which is not a dev-world mob template.");
                }
            }
            return violations;
        }

        private static IEnumerable<T> AllOf<T>(ITemplateRegistry registry) where T : class =>
            registry.AllBlueprintIds()
                .Select(id => registry.TryGet(id, out var t) ? t as T : null)
                .OfType<T>();
    }
}
