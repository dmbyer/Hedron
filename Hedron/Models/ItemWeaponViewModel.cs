using Hedron.Core.Damage;
using Hedron.Core.Entity.Property;

namespace Hedron.Models
{
    public class ItemWeaponViewModel : BaseEntityInanimateViewModel
	{
		public DamageType DamageType { get; set; }

		public int MinDamage { get; set; }// = Constants.DEFAULT_DAMAGE;

		public int MaxDamage { get; set; }// = Constants.DEFAULT_DAMAGE * 2;

		public Affect Affects { get; set; }// = new Affect();
	}
}