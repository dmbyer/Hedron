using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Commands.Events;
using Hedron.Core.Events;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Runtime of the command-Initiator tier. Builds a verb → command map at construction,
    /// then for each dispatch: checks authorization, parses arguments, constructs a
    /// <see cref="CommandContext"/>, calls <see cref="ICommand.ExecuteAsync"/>, and
    /// publishes <see cref="CommandExecutedEvent"/> on every outcome.
    /// </summary>
    public class CommandDispatcher : ICommandDispatcher
    {
        private readonly Dictionary<string, ICommand> _byVerb =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly IAuthorizationChecker _authorizationChecker;
        private readonly ICommandArgumentParser _argumentParser;
        private readonly IOutputWriterFactory _outputWriterFactory;
        private readonly IEventBus _eventBus;
        private readonly ILogger<CommandDispatcher> _logger;
        private readonly IServiceProvider _services;

        public CommandDispatcher(
            IEnumerable<ICommand> commands,
            IAuthorizationChecker authorizationChecker,
            ICommandArgumentParser argumentParser,
            IOutputWriterFactory outputWriterFactory,
            IEventBus eventBus,
            ILogger<CommandDispatcher> logger,
            IServiceProvider services)
        {
            if (commands is null) throw new ArgumentNullException(nameof(commands));
            _authorizationChecker = authorizationChecker ?? throw new ArgumentNullException(nameof(authorizationChecker));
            _argumentParser = argumentParser ?? throw new ArgumentNullException(nameof(argumentParser));
            _outputWriterFactory = outputWriterFactory ?? throw new ArgumentNullException(nameof(outputWriterFactory));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _services = services ?? throw new ArgumentNullException(nameof(services));

            foreach (var command in commands)
            {
                Register(command.Name, command);
                foreach (var alias in command.Aliases)
                    Register(alias, command);
            }
        }

        public async Task DispatchAsync(ISession session, string input)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(input)) return;

            var output = _outputWriterFactory.Create(session);
            var trimmed = input.Trim();
            var splitAt = trimmed.IndexOf(' ');
            var verb = splitAt < 0 ? trimmed : trimmed[..splitAt];
            var rawTail = splitAt < 0 ? string.Empty : trimmed[(splitAt + 1)..].TrimStart();

            if (!_byVerb.TryGetValue(verb, out var command))
            {
                await output.WriteAsync(new PlainMessage(
                    $"Unknown command: {verb}. Type 'help' for a list.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.ParseFailed)
                    .ConfigureAwait(false);
                return;
            }

            // Privilege gate
            foreach (var req in command.RequiredPrivileges)
            {
                if (!_authorizationChecker.IsSatisfied(req, session))
                {
                    await output.WriteAsync(new PlainMessage(
                        "You are not authorized to use that command.", OutputSeverity.Error))
                        .ConfigureAwait(false);
                    await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.Unauthorized)
                        .ConfigureAwait(false);
                    return;
                }
            }

            // Argument parse
            var parseResult = _argumentParser.Parse(command.ArgumentSchema, rawTail);
            if (parseResult is ParseResult.Failure failure)
            {
                await output.WriteAsync(new PlainMessage(
                    $"{failure.Reason} Type 'help {verb}' for usage.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.ParseFailed)
                    .ConfigureAwait(false);
                return;
            }

            var parsedArgs = ((ParseResult.Success)parseResult).Args;
            var argsSummary = BuildArgsSummary(rawTail);
            var context = new CommandContext(session, session.PlayerEntityId, parsedArgs, output, _services);

            try
            {
                await command.ExecuteAsync(context).ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, verb, argsSummary, CommandOutcome.Success)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command {Verb} threw for entity {EntityId}", verb, session.PlayerEntityId);
                await output.WriteAsync(new PlainMessage(
                    "Something went wrong. The error has been logged.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, verb, argsSummary, CommandOutcome.Threw)
                    .ConfigureAwait(false);
            }
        }

        private Task PublishExecutedAsync(uint entityId, string verb, string argsSummary, CommandOutcome outcome)
            => _eventBus.PublishAsync(new CommandExecutedEvent(entityId, verb, argsSummary, outcome));

        private static string BuildArgsSummary(string rawTail)
        {
            const int MaxLength = 200;
            return rawTail.Length <= MaxLength ? rawTail : rawTail[..MaxLength];
        }

        private void Register(string verb, ICommand command)
        {
            if (string.IsNullOrWhiteSpace(verb))
                throw new ArgumentException(
                    $"Command '{command.GetType().Name}' declared a blank verb.", nameof(command));

            if (_byVerb.TryGetValue(verb, out var existing))
                throw new InvalidOperationException(
                    $"Verb '{verb}' is registered by both {existing.GetType().Name} " +
                    $"and {command.GetType().Name}.");

            _byVerb[verb] = command;
        }
    }
}
