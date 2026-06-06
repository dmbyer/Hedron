using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Prompt.Systems
{
    /// <summary>
    /// Domain-aware implementation of <see cref="IPromptSource"/>. Reads entity state and
    /// resource pools on each flush to build a fresh <see cref="PromptMessage"/> (compute-on-read,
    /// per design notes: no caching, no dirty flag, no <c>PromptChangedEvent</c>).
    /// Lives in <c>Core/Modules/Prompt/</c>, not <c>Core/Output/</c>, because it depends on
    /// domain types (<see cref="IEntityStateService"/>, <see cref="IStatSystem"/>) -- INV-2.
    /// </summary>
    public sealed class PromptComposerSystem : IPromptSource
    {
        private readonly IEntityStateService _entityStateService;
        private readonly IStatSystem _statSystem;

        public PromptComposerSystem(IEntityStateService entityStateService, IStatSystem statSystem)
        {
            _entityStateService = entityStateService;
            _statSystem = statSystem;
        }

        public PromptMessage? GetPrompt(uint playerEntityId)
        {
            if (playerEntityId == 0) return null;

            var flags = _entityStateService.GetStates(playerEntityId);

            // Highest-priority flag wins for the state label.
            string? stateLabel = null;
            if (flags.HasFlag(EntityStateFlags.Incapacitated))
                stateLabel = "(Incapacitated)";
            else if (flags.HasFlag(EntityStateFlags.InCombat))
                stateLabel = "(Fighting)";
            else if (flags.HasFlag(EntityStateFlags.Resting))
                stateLabel = "(Resting)";

            var pools = new List<PoolDisplay>();
            AddPool(pools, playerEntityId, "HP",      ScoreId.HpCurrent,      ScoreId.HpMax);
            AddPool(pools, playerEntityId, "Mana",    ScoreId.ManaCurrent,    ScoreId.ManaMax);
            AddPool(pools, playerEntityId, "Stamina", ScoreId.StaminaCurrent, ScoreId.StaminaMax);
            AddPool(pools, playerEntityId, "Astra",   ScoreId.AstraCurrent,   ScoreId.AstraMax);

            return new PromptMessage(stateLabel, pools);
        }

        private void AddPool(List<PoolDisplay> pools, uint entityId, string name,
            ScoreId currentId, ScoreId maxId)
        {
            var max = _statSystem.Get(entityId, maxId);
            if (max == 0) return;
            var current = _statSystem.Get(entityId, currentId);
            pools.Add(new PoolDisplay(name, current, max));
        }
    }
}
