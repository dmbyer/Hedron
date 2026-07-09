using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Systems
{
    /// <summary>
    /// Domain-tier (Items) shared projection seam (INV-19): turns an authored/live item into the
    /// <see cref="PowerSnapshot"/> the core-tier <see cref="IPowerBudgetSystem"/> estimates from.
    /// Both overloads key on <see cref="EquipmentStatBonus"/> — the item's only power-relevant
    /// data — so a template (design-time, no live entity) and a live <see cref="ItemDataComponent"/>
    /// (in-world) project identically. Consumed by the <c>power</c> item path, <c>ItemEditor</c>,
    /// and <c>IBalanceAuditSystem</c> — the single source that replaced three hand-rolled inline
    /// builds (see docs/roadmap/completed/power-model-revision.md, Cross-cutting surfaces).
    /// </summary>
    public interface IItemPowerProjectionSystem
    {
        /// <summary>Projects an authored template (design-time; no live entity required).</summary>
        PowerSnapshot Project(ItemTemplate template);

        /// <summary>Projects a live in-world item entity's component.</summary>
        PowerSnapshot Project(ItemDataComponent component);
    }
}
