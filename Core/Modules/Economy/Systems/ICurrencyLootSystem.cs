namespace Hedron.Core.Modules.Economy.Systems
{
    /// <summary>
    /// Pure domain system that resolves a mob's currency loot roll.
    /// INV-5: never publishes events or calls persistence — returns a result only.
    /// INV-26: all randomness is drawn from the injected <c>IRandom</c> seam.
    /// </summary>
    public interface ICurrencyLootSystem
    {
        /// <summary>
        /// Rolls loot for the given mob entity.
        /// Reads the mob's <c>CurrencyLootComponent</c>; for each configured currency, draws a
        /// uniform inclusive [min, max] amount via the injected <c>IRandom</c>.
        /// A zero or absent range yields no entry for that currency; an absent component
        /// yields an empty result.
        /// </summary>
        /// <param name="mobEntityId">The live mob entity (still alive — call before DestroyEntity).</param>
        /// <returns>
        /// A <see cref="CurrencyLootResult"/> mapping each currency to its rolled amount.
        /// Only non-zero rolls are included.
        /// </returns>
        CurrencyLootResult RollLoot(uint mobEntityId);
    }
}
