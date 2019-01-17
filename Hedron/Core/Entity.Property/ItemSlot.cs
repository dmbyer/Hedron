using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Property
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

		[Display(Name ="One-handed Weapon")]
		OneHandedWeapon,

		[Display(Name = "Two-handed Weapon")]
		TwoHandedWeapon,
		Shield
	}
}
