using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Economy
{
    /// <summary>
    /// Lookup contract for currency family definitions. Extends
    /// <see cref="IRegistry{TKey,TDef}"/> so consumers can call <c>TryGet</c>, <c>Get</c>,
    /// <c>AllIds</c>, and <c>All</c> without depending on the concrete registry type.
    /// </summary>
    public interface ICurrencyRegistry : IRegistry<CurrencyId, CurrencyDefinition>
    {
        // Inherits TryGet, Get, AllIds, and All from IRegistry<CurrencyId, CurrencyDefinition>.
    }

    /// <summary>
    /// Registry of all known currency families, keyed by <see cref="CurrencyId"/>.
    /// Rows are validated at construction: each <see cref="CurrencyDefinition"/> must have a
    /// strictly ascending denomination ladder with base unit = 1 (enforced by
    /// <see cref="CurrencyDefinition"/>'s own constructor).
    ///
    /// Follows the <c>StatRegistry</c> / <c>AspectRegistry</c> precedent: a sealed
    /// <see cref="DefinitionRegistry{TKey,TDef}"/> subclass that supplies its own rows.
    /// </summary>
    public sealed class CurrencyRegistry : DefinitionRegistry<CurrencyId, CurrencyDefinition>, ICurrencyRegistry
    {
        public CurrencyRegistry() : base(CreateRows(), r => r.Id) { }

        private static IEnumerable<CurrencyDefinition> CreateRows() =>
        [
            new CurrencyDefinition(
                CurrencyId.Coin,
                "Coin",
                new List<Denomination>
                {
                    new("copper", 1),
                    new("silver", 10),
                    new("gold",   100),
                }),
        ];
    }
}
