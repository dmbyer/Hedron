using System;
using System.Collections.Generic;

namespace Hedron.Core.Commands
{
    /// <summary>Declares the full argument list for one command.</summary>
    public sealed record CommandArgumentSchema(IReadOnlyList<CommandArgument> Arguments)
    {
        public static readonly CommandArgumentSchema Empty =
            new(Array.Empty<CommandArgument>());
    }
}
