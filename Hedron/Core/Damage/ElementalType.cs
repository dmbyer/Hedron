using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Damage
{
	/// <summary>
	/// Elemental types
	/// </summary>
	[Flags]
	public enum ElementalType
	{
		None  =  0,
		Fire  =  1,
		Ice   =  2,
		Water =  4,
		Earth =  8,
		Air   = 16,
		Acid  = 32
	}
}