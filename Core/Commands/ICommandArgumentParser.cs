namespace Hedron.Core.Commands
{
    /// <summary>
    /// Parses the raw tail of a command input line against a command's declarative schema.
    /// </summary>
    public interface ICommandArgumentParser
    {
        /// <summary>
        /// Parses <paramref name="rawTail"/> against <paramref name="schema"/>.
        /// <paramref name="resolverContext"/> is forwarded to any non-null
        /// <see cref="IArgumentResolver"/> declared on a <see cref="CommandArgument"/>;
        /// no concrete resolver ships until slice 6, but the call-site is wired.
        /// </summary>
        ParseResult Parse(
            CommandArgumentSchema schema,
            string rawTail,
            CommandArgumentResolverContext resolverContext);
    }
}
