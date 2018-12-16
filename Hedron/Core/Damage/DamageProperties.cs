using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;

namespace Hedron.Core.Damage
{
	public class DamageProperties : ICopyableObject<DamageProperties>
	{
		public DamageType    DamageType    { get; set; }
		public ElementalType ElementalType { get; set; }

		public DamageProperties()
		{
			DamageType = new DamageType();
			ElementalType = new ElementalType();
		}

		public void CopyTo(DamageProperties damageProperties)
		{
			DamageType.CopyTo(damageProperties.DamageType);
			ElementalType.CopyTo(damageProperties.ElementalType);
		}
	}
}