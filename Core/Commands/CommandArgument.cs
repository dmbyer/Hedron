using System;

namespace Hedron.Core.Commands
{
    /// <summary>Declares one positional argument in a command's argument schema.</summary>
    public sealed record CommandArgument(
        string Name,
        Type ClrType,
        CommandArgumentKind Kind,
        bool Required,
        string? HelpText,
        IArgumentResolver? Resolver = null);
}
