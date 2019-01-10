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
		public DamageType DamageType    { get; set; }

		/// <summary>
		/// The value of the modifier
		/// </summary>
		public int        Value         { get; set; }

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
		public DamageModifier(DamageType damageType, int value)
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
			damageModifier.DamageType = DamageType;
			damageModifier.Value = Value;
		}
	}
}