using Hedron.Core.Modules.Economy;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="CurrencyFormatter"/>.
    ///
    /// Coverage contract: Score-display formatting postcondition from
    /// docs/implementation-plans/currency-foundation.md (WP-3, Tier 1):
    ///
    ///   base units → full-word denomination string, including multi-denomination
    ///   and zero cases. The conversion is a decision (not prose), tested as a pure function.
    ///
    /// The Coin ladder is copper=1, silver=10, gold=100.
    /// All denominations are always shown (no zero-suppression).
    /// </summary>
    public sealed class CurrencyFormatterTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private static readonly ICurrencyRegistry Registry = new CurrencyRegistry();

        // ── Multi-denomination cases ──────────────────────────────────────────────

        [Fact]
        public void FormatAmount_105_copper_renders_1_gold_0_silver_5_copper()
        {
            var result = CurrencyFormatter.FormatAmount(105L, CurrencyId.Coin, Registry);
            Assert.Equal("1 gold, 0 silver, 5 copper", result);
        }

        [Fact]
        public void FormatAmount_100_copper_renders_1_gold_0_silver_0_copper()
        {
            var result = CurrencyFormatter.FormatAmount(100L, CurrencyId.Coin, Registry);
            Assert.Equal("1 gold, 0 silver, 0 copper", result);
        }

        [Fact]
        public void FormatAmount_110_copper_renders_1_gold_1_silver_0_copper()
        {
            var result = CurrencyFormatter.FormatAmount(110L, CurrencyId.Coin, Registry);
            Assert.Equal("1 gold, 1 silver, 0 copper", result);
        }

        [Fact]
        public void FormatAmount_255_copper_renders_2_gold_5_silver_5_copper()
        {
            var result = CurrencyFormatter.FormatAmount(255L, CurrencyId.Coin, Registry);
            Assert.Equal("2 gold, 5 silver, 5 copper", result);
        }

        // ── Zero case ────────────────────────────────────────────────────────────

        [Fact]
        public void FormatAmount_zero_renders_0_gold_0_silver_0_copper()
        {
            var result = CurrencyFormatter.FormatAmount(0L, CurrencyId.Coin, Registry);
            Assert.Equal("0 gold, 0 silver, 0 copper", result);
        }

        // ── Pure-base-unit cases ──────────────────────────────────────────────────

        [Fact]
        public void FormatAmount_5_copper_renders_0_gold_0_silver_5_copper()
        {
            var result = CurrencyFormatter.FormatAmount(5L, CurrencyId.Coin, Registry);
            Assert.Equal("0 gold, 0 silver, 5 copper", result);
        }

        [Fact]
        public void FormatAmount_10_copper_renders_0_gold_1_silver_0_copper()
        {
            var result = CurrencyFormatter.FormatAmount(10L, CurrencyId.Coin, Registry);
            Assert.Equal("0 gold, 1 silver, 0 copper", result);
        }

        // ── Large amount ──────────────────────────────────────────────────────────

        [Fact]
        public void FormatAmount_1000_copper_renders_10_gold_0_silver_0_copper()
        {
            var result = CurrencyFormatter.FormatAmount(1000L, CurrencyId.Coin, Registry);
            Assert.Equal("10 gold, 0 silver, 0 copper", result);
        }

        // ── CurrencyAwardNarrationHandler.FormatAwardMessage also uses the shared helper ──

        [Fact]
        public void NarrationHandler_FormatAwardMessage_uses_ladder_formatting()
        {
            // Validates that the WP-2 handler's static helper (now upgraded in WP-3)
            // produces full-word output, not the old raw copper string.
            var result = Hedron.Core.Modules.Economy.Handlers.CurrencyAwardNarrationHandler
                .FormatAwardMessage(105L, CurrencyId.Coin, Registry);
            Assert.Equal("You receive 1 gold, 0 silver, 5 copper.", result);
        }
    }
}
