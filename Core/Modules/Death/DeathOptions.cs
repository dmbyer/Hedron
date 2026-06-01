namespace Hedron.Core.Modules.Death
{
    /// <summary>
    /// Settings bound from the <c>Death:</c> configuration section.
    /// </summary>
    public sealed class DeathOptions
    {
        /// <summary>
        /// Minimum HP a player can reach before dying. Default: -10.
        /// Used as the lower clamp floor in <c>IAttributeSystem.SetCurrentHp</c>
        /// and as the death threshold in <c>IDeathSystem.OnHpChanged</c>.
        /// </summary>
        public int HpFloor { get; set; } = -10;

        /// <summary>
        /// HP drained per heartbeat tick while the player is incapacitated. Default: 1.
        /// </summary>
        public int BleedPerTick { get; set; } = 1;

        /// <summary>
        /// Fraction of each pool's maximum restored on respawn. Default: 0.25 (25%).
        /// Applied as <c>floor(Max * RespawnPoolPercent)</c> for every pool.
        /// </summary>
        public double RespawnPoolPercent { get; set; } = 0.25;
    }
}
