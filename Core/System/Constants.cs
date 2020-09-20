using Hedron.Core.Entities.Properties;

namespace Hedron.Core.System
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
		public const int    COMBAT_TICK_TIME      = WORLD_TICK_TIME * 2;
		public const int    COMBAT_TICK_TIME_BOSS = COMBAT_TICK_TIME * 2;

		// Default starting values
		public const int    DEFAULT_POOL          = 100;
		public const int    DEFAULT_ATTRIBUTE     =  10;
		public const int    DEFAULT_CRITICAL      =   0;
		public const int    DEFAULT_DAMAGE        =   5;
		public const int    DEFAULT_ATTACK        =  10;
		public const int    DEFAULT_DEFENSE       =  10;

		// Tier levels
		public const int    MIN_TIER              = 1;
		public const int    MAX_TIER              = 6;

		// Mob levels defined in Entity.Property/Level.cs for now
		// ----

		// Area constants
		public const int    AREA_MANUAL_RESPAWN   = 0;

		// Skill levels
		public const int    MAX_SKILL_LEVEL       = 10000;
		public const int    COOLDOWN_TIME_NONE    = 0;
		public const int    SKILL_UNLEARNED       = -1;

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
			public static int AT(ItemSlot slot)
			{
				return slot switch
				{
					ItemSlot.None => 0,
					ItemSlot.Light => 1,
					ItemSlot.Orbit => 4,
					ItemSlot.Head => 1,
					ItemSlot.Neck => 2,
					ItemSlot.Torso => 1,
					ItemSlot.Arms => 1,
					ItemSlot.Hands => 1,
					ItemSlot.Waist => 1,
					ItemSlot.Legs => 1,
					ItemSlot.Feet => 1,
					ItemSlot.Finger => 2,
					ItemSlot.OneHandedWeapon => 2,
					ItemSlot.TwoHandedWeapon => 1,
					ItemSlot.Shield => 1,
					_ => 0,
				};
			}
		}

		public static class Prompt
		{
			// Public members
			public static readonly string HP_CURRENT      = $"$hp";
			public static readonly string HP_MAX          = $"$HP";
			public static readonly string STAMINA_CURRENT = $"$st";
			public static readonly string STAMINA_MAX     = $"$ST";
			public static readonly string ENERGY_CURRENT  = $"$en";
			public static readonly string ENERGY_MAX      = $"$EN";

			public static readonly string DEFAULT_COLOR   = $"{Text.Formatter.FriendlyColorRed}$hp{Text.Formatter.FriendlyColorReset}/"
				+ $"{Text.Formatter.FriendlyColorRed + Text.Formatter.FriendlyColorBold}$HP{Text.Formatter.FriendlyColorReset}hp "
				+ $"{Text.Formatter.FriendlyColorGreen}$st{Text.Formatter.FriendlyColorReset}/"
				+ $"{Text.Formatter.FriendlyColorGreen + Text.Formatter.FriendlyColorBold}$ST{Text.Formatter.FriendlyColorReset}st "
				+ $"{Text.Formatter.FriendlyColorBlue}$en{Text.Formatter.FriendlyColorReset}/"
				+ $"{Text.Formatter.FriendlyColorBlue + Text.Formatter.FriendlyColorBold}$EN{Text.Formatter.FriendlyColorReset}en > ";

			public static readonly string DEFAULT         = $"$hp/$HPhp $st/$STst $en/$ENen > ";
		}
	}
}