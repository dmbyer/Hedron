using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Sessions;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Default <see cref="ICommandDispatcher"/>: builds a verb → command map from the
    /// DI-registered set of <see cref="ICommand"/>s at construction.
    /// </summary>
    public class CommandDispatcher : ICommandDispatcher
    {
        private readonly Dictionary<string, ICommand> _byVerb =
            new(StringComparer.OrdinalIgnoreCase);

        public CommandDispatcher(IEnumerable<ICommand> commands)
        {
            if (commands is null) throw new ArgumentNullException(nameof(commands));

            foreach (var command in commands)
            {
                Register(command.Name, command);
                foreach (var alias in command.Aliases)
                    Register(alias, command);
            }
        }

        public Task DispatchAsync(ISession session, string input)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(input))
                return Task.CompletedTask;

            var trimmed = input.Trim();
            var splitAt = trimmed.IndexOf(' ');
            var verb = splitAt < 0 ? trimmed : trimmed[..splitAt];
            var args = splitAt < 0 ? string.Empty : trimmed[(splitAt + 1)..].Trim();

            if (_byVerb.TryGetValue(verb, out var command))
                return command.ExecuteAsync(session, args);

            return session.SendLineAsync($"Unknown command: {verb}");
        }

        private void Register(string verb, ICommand command)
        {
            if (string.IsNullOrWhiteSpace(verb))
                throw new ArgumentException(
                    $"Command '{command.GetType().Name}' declared a blank verb.",
                    nameof(command));

            if (_byVerb.TryGetValue(verb, out var existing))
                throw new InvalidOperationException(
                    $"Verb '{verb}' is registered by both {existing.GetType().Name} " +
                    $"and {command.GetType().Name}.");

            _byVerb[verb] = command;
        }
    }
}
