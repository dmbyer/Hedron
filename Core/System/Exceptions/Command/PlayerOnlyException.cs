using Hedron.Core.Commands;

namespace Hedron.Core.System.Exceptions.Command
{
	/// <summary>
	/// For situations where only a player may execute a command due to casting requirements
	/// </summary>
	public class PlayerOnlyException : CommandException
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public PlayerOnlyException()
		{
			CommandResult = CommandResult.PlayerOnly();
		}
	}
}