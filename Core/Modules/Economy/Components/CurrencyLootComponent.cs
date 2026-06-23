using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Economy.Components
{
    /// <summary>
    /// Holds a per-<see cref="CurrencyId"/> loot range (min, max) for a mob entity.
    /// When present and non-zero, <c>ICurrencyLootSystem.RollLoot</c> draws a uniform
    /// inclusive [min, max] amount for each configured currency on mob death.
    ///
    /// <para>
    /// This is a world-content specification authored via YAML / the Blazor editor and
    /// applied by <c>MobTemplate.Apply</c>. It is intentionally <b>NOT</b> tagged
    /// <c>[Persistent]</c> — YAML is its durable form and mobs never carry
    /// <c>PersistentEntity</c> (INV-23). Zero or absent range means no drop (opt-in default).
    /// </para>
    /// </summary>
    public sealed class CurrencyLootComponent : IComponent
    {
        /// <summary>
        /// Per-currency loot ranges in base units. Key is the currency; value is the (min, max)
        /// inclusive range. Missing key or zero max means no drop for that currency.
        /// </summary>
        public Dictionary<CurrencyId, (int Min, int Max)> Ranges { get; set; } = new();
    }
}
