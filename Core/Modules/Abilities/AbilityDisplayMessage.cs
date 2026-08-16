using System.Linq;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Abilities
{
    /// <summary>
    /// One ability's line in <c>skills</c> / <c>spells</c> / <c>abilities</c>.
    ///
    /// <para>
    /// The trailing rank block (<c>rank N  xp A/B</c>) is the visible half of use-based
    /// progression: rank is the ability track's improvement count, <c>A</c> its cumulative XP, and
    /// <c>B</c> the XP that crosses the next threshold. An ability that has never earned shows
    /// rank 0 rather than being omitted, so the player can see what is available to improve.
    /// Rank is <b>display-only</b> — it grants no power this slice (D3).
    /// </para>
    /// </summary>
    public sealed class AbilityDisplayMessage : IOutputMessage
    {
        private readonly AbilityDefinition _def;
        private readonly float _cooldownRemaining;
        private readonly int _rank;
        private readonly int _cumulativeXp;
        private readonly int _xpToNext;

        public OutputCategory Category => OutputCategory.Info;

        public AbilityDisplayMessage(
            AbilityDefinition def,
            float cooldownRemaining,
            int rank = 0,
            int cumulativeXp = 0,
            int xpToNext = 0)
        {
            _def = def;
            _cooldownRemaining = cooldownRemaining;
            _rank = rank;
            _cumulativeXp = cumulativeXp;
            _xpToNext = xpToNext;
        }

        public string Format()
        {
            var costStr = _def.Costs.Count == 0
                ? "none"
                : string.Join(", ", _def.Costs.Select(c => $"{c.Amount} {c.Resource.ToString().ToLower()}"));
            var cooldownStr = _cooldownRemaining > 0f
                ? $"{_cooldownRemaining:F1}s"
                : "ready";
            var rankStr = $"rank {_rank,-3} xp {_cumulativeXp}/{_cumulativeXp + _xpToNext}";
            return $"  {_def.Id,-16} [{_def.Kind,-6}] [{_def.Activation,-9}] [{_def.Targeting,-7}]  cost: {costStr,-20}  cd: {cooldownStr,-6}  {rankStr}";
        }
    }
}
