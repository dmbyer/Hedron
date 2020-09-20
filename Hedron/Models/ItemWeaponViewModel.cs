using Hedron.Core.Damage;
using Hedron.Core.Entities.Properties;
using System.ComponentModel;

namespace Hedron.Models
{
    public class ItemWeaponViewModel : BaseEntityInanimateViewModel
	{
		[DisplayName("Damage Type")]
		public DamageType DamageType { get; set; }

		[DisplayName("Minimum Damage")]
		public int MinDamage { get; set; } // = Constants.DEFAULT_DAMAGE;

		[DisplayName("Maximum Damage")]
		public int MaxDamage { get; set; } // = Constants.DEFAULT_DAMAGE * 2;

		public Affect Affects { get; set; } // = new Affect();

		[DisplayName("Weapon Type")]
		public WeaponType WeaponType { get; set; }
	}
}