using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Systems
{
    /// <summary>
    /// Domain-tier (Mobs) shared projection seam (INV-19): turns an authored mob template into
    /// the <see cref="PowerSnapshot"/> the core-tier <see cref="IPowerBudgetSystem"/> estimates
    /// from. Template-sourced only — <c>power</c>'s live self/mob path keeps its own
    /// <c>IStatSystem</c> snapshot, a distinct correct projection not folded in here (see
    /// docs/roadmap/completed/power-model-revision.md, Postconditions). Consumed by
    /// <c>MobEditor</c> and <c>IBalanceAuditSystem</c>.
    /// </summary>
    public interface IMobPowerProjectionSystem
    {
        PowerSnapshot Project(MobTemplate template);
    }
}
