using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    public sealed class BalanceAuditSystem : IBalanceAuditSystem
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IItemPowerProjectionSystem _itemProjection;
        private readonly IMobPowerProjectionSystem _mobProjection;
        private readonly PowerBudgetTunables _tunables;
        private readonly int _bandDriftTolerance;

        public BalanceAuditSystem(
            ITemplateRegistry templateRegistry,
            IPowerBudgetSystem powerBudget,
            IItemPowerProjectionSystem itemProjection,
            IMobPowerProjectionSystem mobProjection,
            PowerBudgetTunables tunables,
            int bandDriftTolerance)
        {
            _templateRegistry = templateRegistry;
            _powerBudget = powerBudget;
            _itemProjection = itemProjection;
            _mobProjection = mobProjection;
            _tunables = tunables;
            _bandDriftTolerance = bandDriftTolerance;
        }

        public BalanceAuditReport Audit()
        {
            var drifted = new List<BalanceAuditEntry>();
            var bucketCounts = new Dictionary<(int Tier, int Band), int>();

            foreach (var blueprintId in _templateRegistry.AllBlueprintIds())
            {
                if (!_templateRegistry.TryGet(blueprintId, out var template))
                    continue;

                switch (template)
                {
                    case ItemTemplate item:
                    {
                        var power = _powerBudget.Estimate(_itemProjection.Project(item), item.Tier);
                        var computed = _powerBudget.Classify(power);
                        Bucket(bucketCounts, computed);
                        TryAddDrifted(drifted, BalanceAuditKind.Item, blueprintId, item.Tier, item.Band, computed);
                        break;
                    }
                    case MobTemplate mob:
                    {
                        var power = _powerBudget.Estimate(_mobProjection.Project(mob), mob.Tier);
                        var computed = _powerBudget.Classify(power);
                        Bucket(bucketCounts, computed);
                        TryAddDrifted(drifted, BalanceAuditKind.Mob, blueprintId, mob.Tier, mob.Band, computed);
                        break;
                    }
                }
            }

            return new BalanceAuditReport(drifted, bucketCounts);
        }

        private static void Bucket(Dictionary<(int Tier, int Band), int> bucketCounts, PowerBand computed)
        {
            var key = (computed.Tier, computed.Band);
            bucketCounts[key] = bucketCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        // Authored Band 0 (unbanded) is excluded from drift assertion (Open question 5) — such
        // content still contributes to the computed bucket counts above, just carries no
        // authored-vs-computed comparison.
        private void TryAddDrifted(
            List<BalanceAuditEntry> drifted,
            BalanceAuditKind kind,
            string blueprintId,
            int authoredTier,
            int authoredBand,
            PowerBand computed)
        {
            if (authoredBand == 0)
                return;

            var drift = Math.Abs(
                _tunables.GlobalBandIndex(authoredTier, authoredBand) -
                _tunables.GlobalBandIndex(computed.Tier, computed.Band));

            if (drift > _bandDriftTolerance)
            {
                drifted.Add(new BalanceAuditEntry(
                    kind, blueprintId, authoredTier, authoredBand, computed.Tier, computed.Band, drift));
            }
        }
    }
}
