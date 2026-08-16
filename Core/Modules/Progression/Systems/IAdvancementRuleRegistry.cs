using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Systems
{
    /// <summary>
    /// Lookup over the advancement table — one <see cref="AdvancementRule"/> per
    /// <see cref="XpSource"/>. Immutable after construction (INV-31: no shared mutable singleton
    /// state). An <see cref="XpSource"/> with no row is a no-op, not an error: the vocabulary
    /// declares sources ahead of their wiring.
    /// </summary>
    public interface IAdvancementRuleRegistry : IRegistry<XpSource, AdvancementRule> { }
}
