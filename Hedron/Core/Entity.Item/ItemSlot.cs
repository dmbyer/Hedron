using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core
{
	public enum ItemSlot
	{
		// When updating this enum, be sure to update Constants.MAX_EQUIPPED.AT()
		// Item can be equipped as...
		None,
		Light,
		Orbit,
		Head,
		Neck,
		Torso,
		Arms,
		Hands,
		Waist,
		Legs,
		Feet,
		Finger,
		OneHandedWeapon,
		TwoHandedWeapon,
		Shield
	}
}
