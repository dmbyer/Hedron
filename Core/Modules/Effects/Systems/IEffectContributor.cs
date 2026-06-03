using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Effects.Systems
{
    public interface IEffectContributor
    {
        int GetModifiers(uint entityId, ScoreId scoreId);
        IEnumerable<Effect> GetActive(uint entityId);
    }
}
