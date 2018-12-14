using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Damage
{
	/// <summary>
	/// Elemental types
	/// </summary>
	public class ElementalType : ICopyableObject<ElementalType>
	{
		public bool Fire  { get; set; }
		public bool Ice   { get; set; }
		public bool Water { get; set; }
		public bool Earth { get; set; }
		public bool Air   { get; set; }
		public bool Acid  { get; set; }

		/// <summary>
		/// Copies the elemental type to another elemental type
		/// </summary>
		/// <param name="elementalType">The elemental type to copy to</param>
		public void CopyTo(ElementalType elementalType)
		{
			elementalType.Fire = Fire;
			elementalType.Ice = Ice;
			elementalType.Water = Water;
			elementalType.Earth = Earth;
			elementalType.Air = Air;
			elementalType.Acid = Acid;
		}
	}
}