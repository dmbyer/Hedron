using System;

namespace Hedron.System
{
    public class Flags
    {

        // When changing flags, it is critical to remember that any saved files with flags will be impacted.
        // It is highly recommended to not remove any flags, and only append new ones.

		// Flags can be checked with .HasFlag()
        [Flags]
        public enum ItemBehavior
        {
			// Item does not have any special properties (is static)
            NoBehavior  =  0,

			// Item can be picked up into inventory
			Obtainable	=  1,

			// Item can be stored in a container
            Storable	=  2,

			// Item can be equipped
            Equippable	=  4,

			// Item can be a random drop
			RandomDrop  =  8,

			// Item cannot be given to another once a player picks it up
			Bound       = 16
        }

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

		public enum ItemRarity
		{
			Common,
			Uncommon,
			Rare,
			Artifact,
			Unique
		}

        [Flags]
        public enum MobBehavior
        {
            NoBehavior  =  0,
            Aggressive  =  1,
            Scavenge    =  2,
            AutoEquip   =  4,
            AutoPillage =  8,
			Wander      = 16,
			ShopKeeper  = 32
        }

        [Flags]
        public enum PlayerRights
        {
            None   = 0,
			Player = 1,
			Admin  = 2
        }

		[Flags]
		public enum RoomBehavior
		{
			NoBehavior = 0,
			NoWander   = 1
		}
		
	}
}