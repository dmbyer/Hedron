using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Events;
using Hedron.Core.Events;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Handlers
{
    /// <summary>
    /// Writes one structured-log line per command dispatch. Priority
    /// <see cref="HandlerPriority.Notification"/> (80) — deliberately separate from
    /// <c>AdminAuditHandler</c>, which carries richer slice-2 admin-event payloads.
    /// Log level controls verbosity; the event fires for every outcome including failures.
    /// </summary>
    public sealed class CommandLoggingHandler : IEventHandler<CommandExecutedEvent>
    {
        private readonly ILogger<CommandLoggingHandler> _logger;

        public int Priority => HandlerPriority.Notification;

        public CommandLoggingHandler(ILogger<CommandLoggingHandler> logger)
            => _logger = logger;

        public Task HandleAsync(CommandExecutedEvent e)
        {
            _logger.LogInformation(
                "CommandExecuted | invoker={InvokerEntityId} verb={Verb} args={ArgsSummary} outcome={Outcome}",
                e.InvokerEntityId, e.Verb, e.ArgsSummary, e.Outcome);
            return Task.CompletedTask;
        }
    }
}
