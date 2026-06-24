namespace Hedron.Core.Modules.Economy
{
    /// <summary>
    /// Identifies a currency family. Each value corresponds to one <see cref="CurrencyDefinition"/>
    /// row in <see cref="ICurrencyRegistry"/>. New currency families (e.g. Astral marks, faction
    /// tokens) are added here and wired as registry rows — no code changes elsewhere.
    /// </summary>
    public enum CurrencyId
    {
        /// <summary>
        /// The launch currency family. Denominations: copper (base unit, ×1), silver (×10), gold (×100).
        /// All wallet balances are stored as copper (base unit) longs.
        /// </summary>
        Coin,
    }
}
