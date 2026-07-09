using System.Collections.Generic;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public sealed class MobPowerProjectionSystem : IMobPowerProjectionSystem
    {
        public PowerSnapshot Project(MobTemplate template)
        {
            var scores = new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = template.Mind,
                [ScoreId.Body] = template.Body,
                [ScoreId.Spirit] = template.Spirit,
                [ScoreId.Attunement] = template.Attunement,
                [ScoreId.HpMax] = template.MaxHp,
                [ScoreId.ManaMax] = template.MaxMana,
                [ScoreId.StaminaMax] = template.MaxStamina,
                [ScoreId.AstraMax] = template.MaxAstra,
                // Mirrors IStatSystem's base derivations (AttackPower = Body/2, Defense = Body/4) —
                // the template carries no live entity to read those from.
                [ScoreId.AttackPower] = template.Body / 2,
                [ScoreId.Defense] = template.Body / 4,
            };
            return new PowerSnapshot(scores);
        }
    }
}
