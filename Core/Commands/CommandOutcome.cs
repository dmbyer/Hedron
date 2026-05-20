namespace Hedron.Core.Commands
{
    /// <summary>Result classification published on every <c>CommandExecutedEvent</c>.</summary>
    public enum CommandOutcome
    {
        Success,
        ParseFailed,
        Unauthorized,
        Threw,
    }
}
