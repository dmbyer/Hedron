using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Help.Commands
{
    /// <summary>
    /// <c>help</c> — with no argument lists all visible commands; with a verb shows detailed
    /// help for that command. Visibility is filtered by <see cref="IAuthorizationChecker"/>.
    /// </summary>
    public sealed class HelpCommand : ICommand
    {
        private readonly IEnumerable<ICommand> _allCommands;
        private readonly IAuthorizationChecker _authorizationChecker;

        public string Name => "help";
        public IReadOnlyList<string> Aliases { get; } = new[] { "?" };
        public CommandCategory Category => CommandCategory.Player;
        public string ShortDescription => "Show command help.";
        public string LongDescription =>
            "With no argument, lists all commands available to you grouped by category. " +
            "With a verb argument, shows detailed help for that command.";
        public string Usage => "help [<verb>]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("verb", typeof(string), CommandArgumentKind.Token,
                Required: false, "Command verb to look up."),
        });

        public HelpCommand(IEnumerable<ICommand> allCommands, IAuthorizationChecker authorizationChecker)
        {
            _allCommands = allCommands;
            _authorizationChecker = authorizationChecker;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (context.Args.TryGet<string>("verb", out var verb))
            {
                var command = _allCommands
                    .Where(c => IsVisible(c, context))
                    .FirstOrDefault(c =>
                        string.Equals(c.Name, verb, StringComparison.OrdinalIgnoreCase)
                        || c.Aliases.Any(a => string.Equals(a, verb, StringComparison.OrdinalIgnoreCase)));

                if (command is null)
                {
                    await context.Output.WriteAsync(
                        new PlainMessage($"No help found for '{verb}'.", OutputSeverity.System))
                        .ConfigureAwait(false);
                    return;
                }

                await context.Output.WriteAsync(
                    new HelpEntryMessage(command.Name, command.LongDescription, command.Usage))
                    .ConfigureAwait(false);
            }
            else
            {
                var entries = BuildIndex(context);
                await context.Output.WriteAsync(new HelpIndexMessage(entries)).ConfigureAwait(false);
            }
        }

        private IReadOnlyList<HelpIndexEntry> BuildIndex(CommandContext context)
            => _allCommands
                .Where(c => IsVisible(c, context))
                .OrderBy(c => (int)c.Category).ThenBy(c => c.Name)
                .Select(c => new HelpIndexEntry(c.Name, c.ShortDescription, c.Category))
                .ToList();

        private bool IsVisible(ICommand command, CommandContext context)
            => command.RequiredPrivileges.All(r => _authorizationChecker.IsSatisfied(r, context.Session));
    }
}
