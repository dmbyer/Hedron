using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Damage;
using Hedron.Core.Property;
using Hedron.System;

namespace Hedron.Models
{
	public class ItemWeaponViewModel : BaseEntityInanimateViewModel
	{
		public DamageTypeViewModel DamageType { get; set; }

		public ElementalTypeViewModel ElementalType { get; set; }

		public int MinDamage { get; set; }// = Constants.DEFAULT_DAMAGE;

		public int MaxDamage { get; set; }// = Constants.DEFAULT_DAMAGE * 2;

		public Affect Affects { get; set; }// = new Affect();
	}
}