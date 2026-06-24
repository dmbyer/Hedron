using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Economy
{
    /// <summary>
    /// One denomination in a currency ladder — a human-readable name and a base-unit multiplier.
    /// Denominations within a <see cref="CurrencyDefinition"/> are ordered from smallest to largest
    /// (ascending multiplier).
    /// </summary>
    /// <param name="Name">Display name of the denomination (e.g. "copper", "silver", "gold").</param>
    /// <param name="BaseUnitMultiplier">
    /// How many base units equal one of this denomination. The smallest denomination must be 1
    /// (it IS the base unit). Each subsequent denomination must have a strictly larger multiplier.
    /// </param>
    public sealed record Denomination(string Name, long BaseUnitMultiplier);

    /// <summary>
    /// Definition of a currency family: its display name and an ordered denomination ladder.
    /// Validated at construction — the ladder must be strictly ascending with base unit = 1.
    /// </summary>
    public sealed class CurrencyDefinition
    {
        /// <summary>The <see cref="CurrencyId"/> that keys this definition in the registry.</summary>
        public CurrencyId Id { get; }

        /// <summary>Display name of the currency family (e.g. "Coin").</summary>
        public string Name { get; }

        /// <summary>
        /// Denomination ladder, ordered smallest-to-largest by <see cref="Denomination.BaseUnitMultiplier"/>.
        /// The first entry's multiplier must be 1 (it is the base unit; wallet balances are stored
        /// in base units). Each subsequent multiplier must be strictly greater than the previous.
        /// </summary>
        public IReadOnlyList<Denomination> Denominations { get; }

        /// <summary>
        /// Constructs and validates a currency definition.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="denominations"/> is empty, the first denomination's
        /// <see cref="Denomination.BaseUnitMultiplier"/> is not 1, or any subsequent multiplier is
        /// not strictly greater than the preceding one.
        /// </exception>
        public CurrencyDefinition(CurrencyId id, string name, IReadOnlyList<Denomination> denominations)
        {
            if (denominations == null || denominations.Count == 0)
                throw new ArgumentException(
                    $"CurrencyDefinition '{name}' must have at least one denomination.", nameof(denominations));

            if (denominations[0].BaseUnitMultiplier != 1)
                throw new ArgumentException(
                    $"CurrencyDefinition '{name}': the first denomination ('{denominations[0].Name}') " +
                    $"must have BaseUnitMultiplier = 1 (the base unit). " +
                    $"Got {denominations[0].BaseUnitMultiplier}.", nameof(denominations));

            for (int i = 1; i < denominations.Count; i++)
            {
                if (denominations[i].BaseUnitMultiplier <= denominations[i - 1].BaseUnitMultiplier)
                    throw new ArgumentException(
                        $"CurrencyDefinition '{name}': denomination ladder must be strictly ascending. " +
                        $"'{denominations[i].Name}' (×{denominations[i].BaseUnitMultiplier}) is not " +
                        $"greater than '{denominations[i - 1].Name}' (×{denominations[i - 1].BaseUnitMultiplier}).",
                        nameof(denominations));
            }

            Id = id;
            Name = name;
            Denominations = denominations;
        }
    }
}
