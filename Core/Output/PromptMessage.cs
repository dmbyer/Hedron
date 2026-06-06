using System.Collections.Generic;

namespace Hedron.Core.Output
{
    public sealed record PromptMessage(
        string? StateLabel,
        IReadOnlyList<PoolDisplay> Pools) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
