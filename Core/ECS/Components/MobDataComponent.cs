using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class MobDataComponent : IComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;

        /// <summary>
        /// Authored Ascension tier tag, <c>0</c>&#8211;<c>6</c> (0 = unbanded/base). Authored on
        /// <c>MobTemplate</c>/YAML; mechanical threat is emergent from the additive tier baseline
        /// (<c>AscensionConstants.TierBaselineStep</c>) &#8212; this field is a lightweight content
        /// tag, not a power multiplier. Mob entities never carry <c>PersistentEntity</c> (world
        /// content), so despite this component being <c>[Persistent]</c>, the tag never reaches a
        /// snapshot; its durable form is the YAML template, re-applied on each spawn.
        /// </summary>
        public int Tier { get; set; }

        /// <summary>
        /// Authored descriptive Band tag, <c>0</c>&#8211;<c>3</c> (0 = unbanded, 1-3 = low/mid/high
        /// within <see cref="Tier"/>). Purely descriptive — grants no power, gates nothing. Paired
        /// with <see cref="Tier"/> as the Tier×Band classification (see
        /// <c>docs/design/power-model.md</c>); same persistence/durability notes as <see cref="Tier"/>.
        /// </summary>
        public int Band { get; set; }
    }
}
