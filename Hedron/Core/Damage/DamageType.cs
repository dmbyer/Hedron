using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Damage
{
	/// <summary>
	/// Damage types
	/// </summary>
	[Flags]
	public enum DamageType
	{
		Mundane   =  0,
		Slash     =  1,
		Pierce    =  2,
		Blunt     =  4,
		Magic     =  8,
		Elemental = 16,
		Spirit    = 32
	}
}