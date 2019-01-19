using System;
using System.Collections.Generic;
using System.Text;
using Hedron.System;

namespace Hedron.Core.Damage
{
	public class DamageModifier : ICopyableObject<DamageModifier>
	{
		/// <summary>
		/// The DamageType of the modifier
		/// </summary>
		public DamageType? DamageType    { get; set; }

		/// <summary>
		/// The value of the modifier
		/// </summary>
		public float?     Value         { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public DamageModifier()
		{
			
		}

		/// <summary>
		/// DamageModifier constructor
		/// </summary>
		/// <param name="damageType">The DamageType</param>
		/// <param name="value">The value for the DamageType</param>
		public DamageModifier(DamageType? damageType, float value)
		{
			DamageType = damageType;
			Value = value;
		}

		/// <summary>
		/// Copy to another DamageModifier
		/// </summary>
		/// <param name="damageModifier"></param>
		public void CopyTo(DamageModifier damageModifier)
		{
			if (damageModifier != null)
			{
				damageModifier.DamageType = DamageType;
				damageModifier.Value = Value;
			}
		}
	}
}