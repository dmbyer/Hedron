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

		public void CopyTo(DamageProperties damageProperties)
		{
			
		}
	}
}