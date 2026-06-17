using Hedron.Core.Modules.Stats;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// One authored stat contribution an item applies while it is worn: a signed
    /// <paramref name="Magnitude"/> added to the target <see cref="ScoreId"/>. Pure data (INV-3).
    /// Summed on read by <c>EquipmentEffectContributor</c> as a <c>WhileEquipped</c>
    /// <c>StatModifier</c> — never stored as a standalone effect. Keyed by <see cref="ScoreId"/>
    /// so any addressable score (AttackPower, Defense, future +HpMax/speed) is just another row.
    /// </summary>
    public sealed record EquipmentStatBonus(ScoreId TargetScore, int Magnitude);
}
