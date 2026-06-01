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

        /// <summary>
        /// Removes all effects whose <see cref="EffectLifetime"/> is not
        /// <see cref="EffectLifetime.UntilRemoved"/>. Called on player death to expire
        /// timed and source-bound effects while preserving permanent ones (curses, disease, etc.).
        /// </summary>
        void RemoveImpermanent(uint entityId);

        IReadOnlyList<Effect> GetActive(uint entityId);

        int GetModifiers(uint entityId, ScoreId scoreId);

        EffectTickResult AdvanceTick(TimeSpan elapsed);
    }
}
