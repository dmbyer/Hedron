using System;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Minimal context passed to <see cref="IArgumentResolver.GetCandidates"/> at argument-parse
    /// time. Intentionally separate from <see cref="CommandContext"/> because the resolver runs
    /// inside <see cref="ICommandArgumentParser"/>, before <c>CommandContext</c> is constructed.
    /// Resolvers that need room contents or inventory obtain them via <paramref name="Services"/>
    /// as a last resort; prefer constructor injection on the concrete resolver implementation.
    /// </summary>
    public readonly record struct CommandArgumentResolverContext(
        ISession Session,
        uint InvokerEntityId,
        IServiceProvider Services);
}
