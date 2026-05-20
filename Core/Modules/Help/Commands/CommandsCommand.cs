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
    /// <c>commands</c> — terser sibling of <c>help</c>; prints a category-grouped one-line
    /// index without requiring a verb argument. Same visibility filtering.
    /// </summary>
    public sealed class CommandsCommand : ICommand
    {
        private readonly IEnumerable<ICommand> _allCommands;
        private readonly IAuthorizationChecker _authorizationChecker;

        public string Name => "commands";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public string ShortDescription => "List available commands.";
        public string LongDescription => "Lists all commands available to you, grouped by category.";
        public string Usage => "commands";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public CommandsCommand(IEnumerable<ICommand> allCommands, IAuthorizationChecker authorizationChecker)
        {
            _allCommands = allCommands;
            _authorizationChecker = authorizationChecker;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entries = _allCommands
                .Where(c => IsVisible(c, context))
                .OrderBy(c => (int)c.Category).ThenBy(c => c.Name)
                .Select(c => new HelpIndexEntry(c.Name, c.ShortDescription, c.Category))
                .ToList();

            await context.Output.WriteAsync(new HelpIndexMessage(entries)).ConfigureAwait(false);
        }

        private bool IsVisible(ICommand command, CommandContext context)
            => command.RequiredPrivileges.All(r => _authorizationChecker.IsSatisfied(r, context.Session));
    }
}
