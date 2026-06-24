using System.Collections.Generic;

namespace Hedron.Core.Modules.Economy.Systems
{
    /// <summary>
    /// The result of <see cref="ICurrencyLootSystem.RollLoot"/>.
    /// Maps each <see cref="CurrencyId"/> to its rolled base-unit amount.
    /// Only currencies with a non-zero roll are included.
    /// </summary>
    public sealed record CurrencyLootResult(IReadOnlyDictionary<CurrencyId, long> Awards);
}
