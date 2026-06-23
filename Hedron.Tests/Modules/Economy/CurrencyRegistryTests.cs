using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Economy;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="CurrencyRegistry"/> and <see cref="CurrencyDefinition"/>
    /// construction validation.
    ///
    /// Coverage contract: registry-validation throws-test postcondition from
    /// docs/implementation-plans/currency-foundation.md.
    /// </summary>
    public sealed class CurrencyRegistryTests
    {
        // ── CurrencyDefinition validation ─────────────────────────────────────────

        [Fact]
        public void CurrencyDefinition_throws_when_first_denomination_base_unit_is_not_1()
        {
            var denominations = new List<Denomination>
            {
                new("silver", 10),  // base unit should be 1, not 10
                new("gold",   100),
            };

            Assert.Throws<ArgumentException>(
                () => new CurrencyDefinition(CurrencyId.Coin, "Bad", denominations));
        }

        [Fact]
        public void CurrencyDefinition_throws_when_ladder_is_not_strictly_ascending()
        {
            var denominations = new List<Denomination>
            {
                new("copper", 1),
                new("silver", 10),
                new("gold",   10),  // same multiplier as silver — not strictly ascending
            };

            Assert.Throws<ArgumentException>(
                () => new CurrencyDefinition(CurrencyId.Coin, "Bad", denominations));
        }

        [Fact]
        public void CurrencyDefinition_throws_when_second_denomination_is_less_than_first()
        {
            var denominations = new List<Denomination>
            {
                new("copper", 1),
                new("sub-copper", 0),  // strictly less than 1 — invalid
            };

            Assert.Throws<ArgumentException>(
                () => new CurrencyDefinition(CurrencyId.Coin, "Bad", denominations));
        }

        [Fact]
        public void CurrencyDefinition_throws_when_denominations_is_empty()
        {
            var denominations = new List<Denomination>();

            Assert.Throws<ArgumentException>(
                () => new CurrencyDefinition(CurrencyId.Coin, "Empty", denominations));
        }

        [Fact]
        public void CurrencyDefinition_accepts_valid_ascending_ladder_with_base_unit_1()
        {
            var denominations = new List<Denomination>
            {
                new("copper", 1),
                new("silver", 10),
                new("gold",   100),
            };

            // Must not throw.
            var def = new CurrencyDefinition(CurrencyId.Coin, "Coin", denominations);

            Assert.Equal(CurrencyId.Coin, def.Id);
            Assert.Equal("Coin", def.Name);
            Assert.Equal(3, def.Denominations.Count);
        }

        [Fact]
        public void CurrencyDefinition_accepts_single_denomination_with_base_unit_1()
        {
            var denominations = new List<Denomination>
            {
                new("credit", 1),
            };

            // Single denomination is valid: the one entry is both the base unit and the only tier.
            var def = new CurrencyDefinition(CurrencyId.Coin, "Credits", denominations);

            Assert.Single(def.Denominations);
        }

        // ── CurrencyRegistry — happy path ─────────────────────────────────────────

        [Fact]
        public void CurrencyRegistry_constructs_without_throwing()
        {
            // Must not throw — validates all rows at construction.
            var registry = new CurrencyRegistry();

            Assert.NotNull(registry);
        }

        [Fact]
        public void CurrencyRegistry_contains_Coin_entry()
        {
            var registry = new CurrencyRegistry();

            Assert.True(registry.TryGet(CurrencyId.Coin, out var def));
            Assert.NotNull(def);
            Assert.Equal(CurrencyId.Coin, def!.Id);
        }

        [Fact]
        public void CurrencyRegistry_Coin_has_base_unit_1_as_first_denomination()
        {
            var registry = new CurrencyRegistry();
            var def = registry.Get(CurrencyId.Coin);

            Assert.Equal(1L, def.Denominations[0].BaseUnitMultiplier);
            Assert.Equal("copper", def.Denominations[0].Name);
        }

        [Fact]
        public void CurrencyRegistry_Coin_denomination_ladder_is_strictly_ascending()
        {
            var registry = new CurrencyRegistry();
            var def = registry.Get(CurrencyId.Coin);

            for (int i = 1; i < def.Denominations.Count; i++)
            {
                Assert.True(
                    def.Denominations[i].BaseUnitMultiplier > def.Denominations[i - 1].BaseUnitMultiplier,
                    $"Denomination '{def.Denominations[i].Name}' (×{def.Denominations[i].BaseUnitMultiplier}) " +
                    $"must be strictly greater than '{def.Denominations[i - 1].Name}' " +
                    $"(×{def.Denominations[i - 1].BaseUnitMultiplier}).");
            }
        }

        [Fact]
        public void CurrencyRegistry_Coin_has_copper_silver_gold_denominations()
        {
            var registry = new CurrencyRegistry();
            var def = registry.Get(CurrencyId.Coin);

            Assert.Equal(3, def.Denominations.Count);
            Assert.Equal("copper", def.Denominations[0].Name);
            Assert.Equal(1L,       def.Denominations[0].BaseUnitMultiplier);
            Assert.Equal("silver", def.Denominations[1].Name);
            Assert.Equal(10L,      def.Denominations[1].BaseUnitMultiplier);
            Assert.Equal("gold",   def.Denominations[2].Name);
            Assert.Equal(100L,     def.Denominations[2].BaseUnitMultiplier);
        }

        [Fact]
        public void CurrencyRegistry_ICurrencyRegistry_is_assignable()
        {
            ICurrencyRegistry registry = new CurrencyRegistry();
            Assert.NotNull(registry.Get(CurrencyId.Coin));
        }
    }
}
