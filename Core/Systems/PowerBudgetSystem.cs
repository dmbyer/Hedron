namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core-tier (INV-2) implementation of <see cref="IPowerBudgetSystem"/>. Imports no
    /// <c>Core/Modules/&lt;Feature&gt;/</c> domain type — every input is either the
    /// constructor-supplied <see cref="PowerBudgetTunables"/> plain-data record or the
    /// caller-supplied <see cref="PowerSnapshot"/>. The tunables record is the one permitted
    /// constructor dependency under the snapshot-only extensibility principle (see
    /// <c>docs/design/power-model.md</c>) — never a registry, loader, or domain reference.
    /// </summary>
    /// <remarks>
    /// The formulas live in <see cref="PowerBudgetMath"/> (pure statics over a caller-supplied
    /// tunables record); this class is the instance facade that binds them to the snapshot the host
    /// composed at startup. Callers that must evaluate against *other* tunables — the standards
    /// editor's per-cell preview of unsaved candidates — call <see cref="PowerBudgetMath"/> directly
    /// rather than constructing a throwaway system.
    /// </remarks>
    public sealed class PowerBudgetSystem : IPowerBudgetSystem
    {
        private readonly PowerBudgetTunables _tunables;

        public PowerBudgetSystem(PowerBudgetTunables tunables)
        {
            _tunables = tunables;
        }

        public int Estimate(PowerSnapshot snapshot, int tier = 0)
            => PowerBudgetMath.Estimate(_tunables, snapshot, tier);

        public PowerBand Classify(int power)
            => PowerBudgetMath.Classify(_tunables, power);

        public PowerRange TargetRange(int tier, int band)
            => PowerBudgetMath.TargetRange(_tunables, tier, band);

        public int BandAnchor(int tier)
            => PowerBudgetMath.BandAnchor(_tunables, tier);
    }
}
