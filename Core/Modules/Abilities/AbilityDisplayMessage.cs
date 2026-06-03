using System.Linq;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Abilities
{
    public sealed class AbilityDisplayMessage : IOutputMessage
    {
        private readonly AbilityDefinition _def;
        private readonly float _cooldownRemaining;

        public OutputCategory Category => OutputCategory.Info;

        public AbilityDisplayMessage(AbilityDefinition def, float cooldownRemaining)
        {
            _def = def;
            _cooldownRemaining = cooldownRemaining;
        }

        public string Format()
        {
            var costStr = _def.Costs.Count == 0
                ? "none"
                : string.Join(", ", _def.Costs.Select(c => $"{c.Amount} {c.Resource.ToString().ToLower()}"));
            var cooldownStr = _cooldownRemaining > 0f
                ? $"{_cooldownRemaining:F1}s"
                : "ready";
            return $"  {_def.Id,-16} [{_def.Kind,-6}] [{_def.Activation,-9}] [{_def.Targeting,-7}]  cost: {costStr,-20}  cd: {cooldownStr}";
        }
    }
}
