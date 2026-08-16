using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Systems
{
    /// <summary>
    /// The shipped advancement table, sourced from <see cref="ProgressionConstants.Rules"/>.
    /// Mirrors <c>AbilityRegistry</c>'s compiled-rows posture (Spine F registry shape).
    /// </summary>
    public sealed class AdvancementRuleRegistry : DefinitionRegistry<XpSource, AdvancementRule>, IAdvancementRuleRegistry
    {
        public AdvancementRuleRegistry() : base(ProgressionConstants.Rules, rule => rule.Source) { }
    }
}
