using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Effects.Systems
{
    public interface IEffectSystem
    {
        Effect? Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId);

        void Remove(uint entityId, string effectId);
        void RemoveByCategory(uint entityId, EffectCategory category);

        IReadOnlyList<Effect> GetActive(uint entityId);

        int GetModifiers(uint entityId, ScoreId scoreId);

        EffectTickResult AdvanceTick(TimeSpan elapsed);
    }
}
