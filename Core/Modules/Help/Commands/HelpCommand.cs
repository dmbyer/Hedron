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
    /// help for that command. Verb lookup uses the same two-phase resolution as
    /// <see cref="CommandDispatcher"/> (exact first, prefix second) via <see cref="IVerbRegistry"/>
    /// so that <c>help lo</c> displays help for <c>look</c> exactly as <c>lo</c> would dispatch.
    /// Each command's declared aliases are shown so players can discover shorthand forms.
    /// </summary>
    public sealed class HelpCommand : ICommand
    {
        private readonly Lazy<IEnumerable<ICommand>> _allCommands;
        private readonly Lazy<IVerbRegistry> _verbRegistry;
        private readonly IAuthorizationChecker _authorizationChecker;

        public string Name => "help";
        public IReadOnlyList<string> Aliases { get; } = new[] { "?" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "Show command help.";
        public string LongDescription =>
            "With no argument, lists all commands available to you grouped by category. " +
            "With a verb argument, shows detailed help for that command. " +
            "Partial verb names are accepted: 'help lo' displays help for 'look'.";
        public string Usage => "help [<verb>]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("verb", typeof(string), CommandArgumentKind.Token,
                Required: false, "Command verb to look up."),
        });

        public HelpCommand(
            Lazy<IEnumerable<ICommand>> allCommands,
            Lazy<IVerbRegistry> verbRegistry,
            IAuthorizationChecker authorizationChecker)
        {
            _allCommands = allCommands;
            _verbRegistry = verbRegistry;
            _authorizationChecker = authorizationChecker;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (context.Args.TryGet<string>("verb", out var verb))
            {
                await ShowCommandHelp(context, verb!).ConfigureAwait(false);
            }
            else
            {
                var entries = BuildIndex(context);
                await context.Output.WriteAsync(new HelpIndexMessage(entries)).ConfigureAwait(false);
            }
        }

        private async Task ShowCommandHelp(CommandContext context, string verb)
        {
            var registry = _verbRegistry.Value;

            // Phase 1: exact match (primary name or alias).
            if (!registry.TryGetExact(verb, out var command))
            {
                // Phase 2: prefix resolution — delegates to IVerbRegistry so the filter
                // logic is not duplicated between HelpCommand and CommandDispatcher.
                var candidates = registry.GetPrefixCandidates(verb);

                switch (candidates.Count)
                {
                    case 0:
                        await context.Output.WriteAsync(
                            new PlainMessage($"No help found for '{verb}'.", OutputSeverity.System))
                            .ConfigureAwait(false);
                        return;

                    case 1:
                        command = candidates[0];
                        break;

                    default:
                        // Filter to visible candidates before listing.
                        var visible = candidates.Where(c => IsVisible(c, context)).ToList();
                        if (visible.Count == 0)
                        {
                            await context.Output.WriteAsync(
                                new PlainMessage($"No help found for '{verb}'.", OutputSeverity.System))
                                .ConfigureAwait(false);
                            return;
                        }
                        if (visible.Count == 1)
                        {
                            command = visible[0];
                            break;
                        }
                        var names = string.Join(", ", visible.Select(c => c.Name));
                        await context.Output.WriteAsync(
                            new PlainMessage($"Ambiguous command '{verb}'. Did you mean: {names}?",
                                OutputSeverity.System))
                            .ConfigureAwait(false);
                        return;
                }
            }

            // Visibility gate — don't reveal admin commands to players.
            if (!IsVisible(command!, context))
            {
                await context.Output.WriteAsync(
                    new PlainMessage($"No help found for '{verb}'.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(
                new HelpEntryMessage(command!.Name, command.LongDescription, command.Usage, command.Aliases))
                .ConfigureAwait(false);
        }

        private IReadOnlyList<HelpIndexEntry> BuildIndex(CommandContext context)
            => _allCommands.Value
                .Where(c => IsVisible(c, context))
                .OrderBy(c => (int)c.Category).ThenBy(c => c.Name)
                .Select(c => new HelpIndexEntry(c.Name, c.ShortDescription, c.Category, c.Aliases))
                .ToList();

        private bool IsVisible(ICommand command, CommandContext context)
            => command.RequiredPrivileges.All(r => _authorizationChecker.IsSatisfied(r, context.Session));
    }
}
