using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Damage
{
	/// <summary>
	/// Damage types
	/// </summary>
	public class DamageType : ICopyableObject<DamageType>
	{
		public bool Slash     { get; set; }
		public bool Pierce    { get; set; }
		public bool Blunt     { get; set; }
		public bool Magic     { get; set; }
		public bool Elemental { get; set; }
		public bool Spirit    { get; set; }

		/// <summary>
		/// Copies the damage type to another damage type
		/// </summary>
		/// <param name="damageType">The damage type to copy to</param>
		public void CopyTo(DamageType damageType)
		{
			damageType.Slash = Slash;
			damageType.Pierce = Pierce;
			damageType.Blunt = Blunt;
			damageType.Magic = Magic;
			damageType.Elemental = Elemental;
			damageType.Spirit = Spirit;
		}
	}
}