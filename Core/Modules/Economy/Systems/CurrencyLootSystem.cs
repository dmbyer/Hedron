using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Economy.Systems
{
    /// <summary>
    /// Domain system that resolves a mob's currency loot roll.
    /// INV-5: pure — returns results only; never touches the event bus or persistence.
    /// INV-1/INV-2: depends only on <see cref="EntityService"/> (ECS layer) and
    ///   <see cref="IRandom"/> (core system) — no upward calls.
    /// INV-26: all randomness drawn from injected <see cref="IRandom"/>.
    /// </summary>
    public sealed class CurrencyLootSystem : ICurrencyLootSystem
    {
        private readonly EntityService _ecs;
        private readonly IRandom _random;

        public CurrencyLootSystem(EntityService ecs, IRandom random)
        {
            _ecs = ecs;
            _random = random;
        }

        /// <inheritdoc/>
        public CurrencyLootResult RollLoot(uint mobEntityId)
        {
            // Absent component → empty result (opt-in default: no drop).
            if (!_ecs.TryGet<CurrencyLootComponent>(mobEntityId, out var loot))
                return new CurrencyLootResult(new Dictionary<CurrencyId, long>());

            var awards = new Dictionary<CurrencyId, long>();

            foreach (var (currency, range) in loot!.Ranges)
            {
                var (min, max) = range;

                // Zero or inverted range → no drop for this currency.
                if (min <= 0 && max <= 0) continue;
                if (max <= 0) continue;

                // Clamp min to 0 so a misconfigured negative min doesn't error.
                var safeMin = min < 0 ? 0 : min;

                // IRandom.Next(min, max) is max-exclusive → add 1 for inclusive [min, max].
                var rolled = _random.Next(safeMin, max + 1);

                if (rolled > 0)
                    awards[currency] = rolled;
            }

            return new CurrencyLootResult(awards);
        }
    }
}
