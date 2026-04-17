using Hedron.Core.Damage;
using Hedron.Core.ECS;
using Hedron.Core.System;
using Core.ECS.Properties;

namespace Hedron.Core.ECS.Components
{
	/// <summary>
	/// Component for weapon-specific data
	/// </summary>
	public class WeaponDataComponent : IComponent
	{
		/// <summary>
		/// The type of damage this weapon deals
		/// </summary>
		public DamageType DamageType { get; set; } = DamageType.Slash;

		/// <summary>
		/// Minimum weapon damage
		/// </summary>
		public int MinDamage { get; set; } = Constants.DEFAULT_DAMAGE;

		/// <summary>
		/// Maximum weapon damage
		/// </summary>
		public int MaxDamage { get; set; } = Constants.DEFAULT_DAMAGE * 2;

		/// <summary>
		/// The type of weapon (sword, axe, etc.)
		/// </summary>
		public WeaponType WeaponType { get; set; } = WeaponType.Sword;
	}
}