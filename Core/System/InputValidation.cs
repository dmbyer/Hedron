using System.Linq;

namespace Hedron.Core.System
{
    public static class InputValidation
	{
		/// <summary>
		///  Validates a player name
		/// </summary>
		/// <param name="playerName">The player name to validate</param>
		/// <returns>Whether the name is valid</returns>
		public static bool ValidPlayerName(string playerName)
		{
			return playerName.ToCharArray().All(x => char.IsLetter(x)) && playerName.Length > 2 && playerName.Length < 13;
		}
	}
}