using System.Collections.Generic;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// Folds progression power into the effect modifier pipeline (the INV-24 contributor seam).
    /// Each track's power step (<see cref="ProgressionConstants.PowerPerImprovement"/> ×
    /// improvement count) is derived on read from <see cref="IProgressionSystem"/> — never stored.
    /// Registered as an <see cref="IEffectContributor"/> alongside <c>EquipmentEffectContributor</c>
    /// and <c>AbilityEffectContributor</c>; <c>EffectSystem.GetModifiers</c> sums it, and
    /// <c>IStatSystem.Get</c> folds that on top of base + equipment + abilities for every consumer.
    ///
    /// <para>
    /// Reuses the existing core-owned <see cref="IEffectContributor"/> port rather than a parallel
    /// <c>IProgressionContributor</c> the program brief names — <c>IStatSystem.Get</c> already
    /// folds exactly one aggregation path (<c>IEffectSystem.GetModifiers</c>'s DI-collected
    /// contributor list); a second port would need <c>IStatSystem</c> re-plumbed to also fold it.
    /// Owner-approved 2026-07-04 (see roadmap/completed/progression-substrate.md Decisions).
    /// </para>
    ///
    /// <para>
    /// <b>Ability tracks contribute nothing (D3).</b> Both members below fold only
    /// <see cref="ScoreId"/>-keyed tracks — <see cref="GetModifiers"/>'s input is a
    /// <see cref="ScoreId"/> by signature, and <see cref="GetActive"/> enumerates
    /// <c>GetTrackedScores</c>, which excludes ability tracks by construction. Ability rank is
    /// display-only this slice; making rank scale potency or cost is a deliberate later balance
    /// slice that must fold into <c>docs/design/power-model.md</c> and re-pin goldens. An
    /// architecture-guard test pins the zero contribution.
    /// </para>
    /// </summary>
    public sealed class ProgressionEffectContributor : IEffectContributor
    {
        private readonly IProgressionSystem _progression;

        public ProgressionEffectContributor(IProgressionSystem progression)
        {
            _progression = progression;
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
            => ProgressionConstants.PowerPerImprovement * _progression.GetImprovementCount(entityId, scoreId);

        public IEnumerable<Effect> GetActive(uint entityId)
        {
            foreach (var track in _progression.GetTrackedScores(entityId))
            {
                var improvements = _progression.GetImprovementCount(entityId, track);
                if (improvements <= 0)
                    continue;

                var power = ProgressionConstants.PowerPerImprovement * improvements;
                yield return new Effect(
                    $"progression.{track}", EffectKind.StatModifier,
                    new EffectParams(track, power),
                    EffectCategory.Buff, power,
                    new EffectSource(entityId),
                    null, EffectLifetime.WhileKnown,
                    0f, 0f,
                    StackPolicy.Stack, EffectPhase.Normal);
            }
        }
    }
}
