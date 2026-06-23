using System.Text;
using Hedron.Core.Modules.Economy;

namespace Hedron.Core.Modules.Economy
{
    /// <summary>
    /// Shared presentation helper: converts a base-unit balance into a full-word denomination
    /// string by walking the <see cref="ICurrencyRegistry"/> ladder from largest to smallest.
    ///
    /// <para>
    /// The output uses full words (e.g. <c>"1 gold, 0 silver, 5 copper"</c>) with all
    /// denominations in the ladder always shown (zero-suppression is intentionally absent — the
    /// full ladder read is the simplest, most auditable form and avoids edge cases such as
    /// "0 copper" being elided from a zero balance).
    /// </para>
    ///
    /// <para>
    /// This is a presentation concern — it does not belong in any system. It is kept here
    /// (co-located with the Economy types) so the <c>TelnetOutputFormatter</c> and
    /// <c>CurrencyAwardNarrationHandler</c> can share a single implementation without either
    /// taking a dependency on the other's assembly.
    /// </para>
    /// </summary>
    public static class CurrencyFormatter
    {
        /// <summary>
        /// Formats <paramref name="baseAmount"/> base units of <paramref name="currency"/> into
        /// a human-readable denomination string using the ladder from <paramref name="registry"/>.
        /// All denominations in the ladder are included, from largest to smallest.
        /// </summary>
        /// <example>
        /// For the Coin family (gold=100, silver=10, copper=1):
        /// <c>FormatAmount(105, CurrencyId.Coin, registry)</c> → <c>"1 gold, 0 silver, 5 copper"</c>
        /// </example>
        public static string FormatAmount(long baseAmount, CurrencyId currency, ICurrencyRegistry registry)
        {
            if (!registry.TryGet(currency, out var definition))
                return $"{baseAmount} (unknown currency)";

            var denominations = definition!.Denominations;

            // Walk largest-to-smallest, computing each denomination's count.
            // denominations is ordered smallest-to-largest (ascending multiplier), so we iterate in reverse.
            var sb = new StringBuilder();
            long remaining = baseAmount;
            bool first = true;

            for (int i = denominations.Count - 1; i >= 0; i--)
            {
                var denom = denominations[i];
                long count = remaining / denom.BaseUnitMultiplier;
                remaining %= denom.BaseUnitMultiplier;

                if (!first) sb.Append(", ");
                sb.Append(count);
                sb.Append(' ');
                sb.Append(denom.Name);
                first = false;
            }

            return sb.ToString();
        }
    }
}
