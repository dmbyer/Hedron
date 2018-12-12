// Extensions to Hedron Common classes for the game specifically
// Primarily for adding IO and Player code handling
namespace Hedron.System
{
	public static partial class Guard
	{
		public static bool IsPlayer(object argumentValue)
		{
			if (argumentValue == null) { return false; }
			else { return argumentValue.GetType() == typeof(Player); }
		}
	}
}