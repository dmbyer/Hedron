using System;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    /// <summary>Default <see cref="IMobPowerReadoutSystem"/>. Pure: returns a result, writes nothing.</summary>
    public sealed class MobPowerReadoutSystem : IMobPowerReadoutSystem
    {
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IMobPowerProjectionSystem _projection;
        private readonly IBalanceStandardsRegistry _standards;

        public MobPowerReadoutSystem(
            IPowerBudgetSystem powerBudget,
            IMobPowerProjectionSystem projection,
            IBalanceStandardsRegistry standards)
        {
            _powerBudget = powerBudget;
            _projection = projection;
            _standards = standards;
        }

        public MobPowerReadout Read(MobTemplate template)
        {
            var power = _powerBudget.Estimate(_projection.Project(template), template.Tier);
            var computed = _powerBudget.Classify(power);

            var drift = Math.Abs(
                _standards.Tunables.GlobalBandIndex(template.Tier, template.Band) -
                _standards.Tunables.GlobalBandIndex(computed.Tier, computed.Band));

            return new MobPowerReadout(
                power,
                computed,
                template.Tier,
                template.Band,
                TargetRangeOrNull(template.Tier, template.Band),
                // Authored band 0 is "unbanded", not "band zero" — it carries no claim to compare
                // against (the rule IBalanceAuditSystem's sweep applies).
                DriftsFromAuthoredCell: template.Band != 0 && drift > _standards.BandDriftTolerance);
        }

        /// <summary>
        /// The authored cell's target window, or <c>null</c> when it has none. Band 0 is unbanded;
        /// a tier or band outside the standards table is an authored value the oracle fails fast on,
        /// and a readout is a display, not a place to surface that as an exception.
        /// </summary>
        private PowerRange? TargetRangeOrNull(int tier, int band)
        {
            if (band == 0)
                return null;

            try
            {
                return _powerBudget.TargetRange(tier, band);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
}
