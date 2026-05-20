using System;
using Hedron.Core.Output;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Passed to every <see cref="ICommand.ExecuteAsync"/> invocation. Replaces the old
    /// <c>(ISession, string)</c> pair with typed parsed arguments and a typed output writer.
    /// </summary>
    public sealed record CommandContext(
        ISession Session,
        uint InvokerEntityId,
        ParsedArguments Args,
        IOutputWriter Output,
        IServiceProvider Services);
}
