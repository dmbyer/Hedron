using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Systems
{
    public sealed class ItemPowerProjectionSystem : IItemPowerProjectionSystem
    {
        public PowerSnapshot Project(ItemTemplate template)
            => Project(template.StatBonuses);

        public PowerSnapshot Project(ItemDataComponent component)
            => Project(component.StatBonuses);

        private static PowerSnapshot Project(IReadOnlyList<EquipmentStatBonus> statBonuses)
        {
            var scores = new Dictionary<ScoreId, int>();
            foreach (var bonus in statBonuses)
                scores[bonus.TargetScore] = bonus.Magnitude;
            return new PowerSnapshot(scores);
        }
    }
}
