namespace Hedron.Core.Commands
{
    /// <summary>
    /// Parses the raw tail of a command input line against a command's declarative schema.
    /// </summary>
    public interface ICommandArgumentParser
    {
        ParseResult Parse(CommandArgumentSchema schema, string rawTail);
    }
}
