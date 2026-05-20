namespace Hedron.Core.Commands
{
    /// <summary>Discriminated union returned by <see cref="ICommandArgumentParser"/>.</summary>
    public abstract record ParseResult
    {
        public sealed record Success(ParsedArguments Args) : ParseResult;
        public sealed record Failure(string Reason) : ParseResult;
    }
}
