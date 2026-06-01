namespace Hedron.Core.Commands
{
    /// <summary>Result classification published on every <c>CommandExecutedEvent</c>.</summary>
    public enum CommandOutcome
    {
        Success,
        ParseFailed,
        Unauthorized,
        Threw,
        /// <summary>
        /// The command was refused because the invoker is currently incapacitated and
        /// the command does not have <c>UsableWhileIncapacitated = true</c>.
        /// </summary>
        Refused,
    }
}
