using System.Collections.Generic;

namespace Hedron.System
{
    public static class Constants
    {
		// Default file/folder locations for save/load
		public const string DEFAULT_WORLD_FOLDER  = @"C:\Hedron";
		public const string DEFAULT_PLAYER_FOLDER = @"C:\Hedron";

		// World data
		public const string DEFAULT_WORLD_NAME    = "Hedron";

		// Value in ms for game loop and combat loops
		public const int    WORLD_TICK_TIME       = 1000;
		public const int    COMBAT_TICK_TIME      = WORLD_TICK_TIME;
		public const int    COMBAT_TICK_TIME_BOSS = COMBAT_TICK_TIME * 2;

		// Server constants
		public const int    SERVER_LISTEN_PORT    = 4000;
		public const string SERVER_LISTEN_IP      = "127.0.0.1";
		public const int    MAX_NET_READS         = 8;
		public enum IO_READ
		{
			SENDEXCEED = -1,
			PENDINGREAD,
			SUCCESSREAD
		}

		// Default starting values
		public const int    DEFAULT_ASPECT        = 10;
		public const int    DEFAULT_ATTRIBUTE     = 10;
		public const int    DEFAULT_CRITICAL      =  0;
		public const int    DEFAULT_DAMAGE        =  5;

		// Affects
		public const int   LIFESPAN_PERMANENT     = -1;

		// Tier levels
		public const int    MIN_TIER              = 1;
		public const int    MAX_TIER              = 6;

		// Directions rooms can have
		public enum EXIT
		{
			NORTH,
			EAST,
			SOUTH,
			WEST,
			UP,
			DOWN
		}

		public static class MAX_EQUIPPED
		{
			/// <summary>
			/// Gives the maximum number of items equipped for a given slot
			/// </summary>
			/// <param name="slot">The slot to check</param>
			/// <returns>The number of maximum equipped items</returns>
			public static int AT(Flags.ItemSlot slot)
			{
				switch (slot)
				{
					case Flags.ItemSlot.None:
						return 0;
					case Flags.ItemSlot.Light:
						return 1;
					case Flags.ItemSlot.Orbit:
						return 4;
					case Flags.ItemSlot.Head:
						return 1;
					case Flags.ItemSlot.Neck:
						return 2;
					case Flags.ItemSlot.Torso:
						return 1;
					case Flags.ItemSlot.Arms:
						return 1;
					case Flags.ItemSlot.Hands:
						return 1;
					case Flags.ItemSlot.Waist:
						return 1;
					case Flags.ItemSlot.Legs:
						return 1;
					case Flags.ItemSlot.Feet:
						return 1;
					case Flags.ItemSlot.Finger:
						return 2;
					case Flags.ItemSlot.OneHandedWeapon:
						return 2;
					case Flags.ItemSlot.TwoHandedWeapon:
						return 1;
					case Flags.ItemSlot.Shield:
						return 1;
					default:
						return 0;
				}
			}
		}

		public static class Prompt
		{
			// Public members
			public static readonly string HP_CURRENT      = @"$hp";
			public static readonly string HP_MAX          = @"$HP";
			public static readonly string STAMINA_CURRENT = @"$st";
			public static readonly string STAMINA_MAX     = @"$ST";
			public static readonly string ENERGY_CURRENT  = @"$en";
			public static readonly string ENERGY_MAX      = @"$EN";
			public static readonly string DEFAULT         = @"$hp/$HPhp $st/$STst $en/$ENen > ";
		}
	}
}