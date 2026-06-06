using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Help.Commands
{
    /// <summary>
    /// <c>help</c> — with no argument lists all visible commands; with a verb shows detailed
    /// help for that command. Verb lookup uses the same two-phase resolution as
    /// <see cref="CommandDispatcher"/> (exact first, prefix second) via <see cref="IVerbRegistry"/>
    /// so that <c>help lo</c> displays help for <c>look</c> exactly as <c>lo</c> would dispatch.
    /// Each command's declared aliases are shown so players can discover shorthand forms.
    /// <para>
    /// When no command matches the topic, falls through to <see cref="IAbilityRegistry"/> so that
    /// <c>help kick</c> displays detailed ability information. When the topic is <c>skills</c>,
    /// <c>spells</c>, or <c>abilities</c>, the command help is shown and a global catalog of all
    /// registered abilities of that kind is appended.
    /// </para>
    /// </summary>
    public sealed class HelpCommand : ICommand
    {
        private readonly Lazy<IEnumerable<ICommand>> _allCommands;
        private readonly Lazy<IVerbRegistry> _verbRegistry;
        private readonly IAuthorizationChecker _authorizationChecker;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "help";
        public IReadOnlyList<string> Aliases { get; } = new[] { "?" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "Show command help.";
        public string LongDescription =>
            "With no argument, lists all commands available to you grouped by category. " +
            "With a verb argument, shows detailed help for that command. " +
            "Partial verb names are accepted: 'help lo' displays help for 'look'. " +
            "If no command matches, searches the ability registry: 'help kick' shows kick's details. " +
            "Typing 'help skills', 'help spells', or 'help abilities' also appends a global catalog.";
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
            IAuthorizationChecker authorizationChecker,
            IAbilityRegistry abilityRegistry)
        {
            _allCommands = allCommands;
            _verbRegistry = verbRegistry;
            _authorizationChecker = authorizationChecker;
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
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
                        // Fall through to ability registry before giving up.
                        await TryShowAbilityHelpAsync(context, verb).ConfigureAwait(false);
                        return;

                    case 1:
                        command = candidates[0];
                        break;

                    default:
                        // Filter to visible candidates before listing.
                        var visible = candidates.Where(c => IsVisible(c, context)).ToList();
                        if (visible.Count == 0)
                        {
                            await TryShowAbilityHelpAsync(context, verb).ConfigureAwait(false);
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
                                OutputSeverity.System, OutputCategory.Help))
                            .ConfigureAwait(false);
                        return;
                }
            }

            // Visibility gate — don't reveal admin commands to players.
            if (!IsVisible(command!, context))
            {
                await TryShowAbilityHelpAsync(context, verb).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(
                new HelpEntryMessage(command!.Name, command.LongDescription, command.Usage, command.Aliases))
                .ConfigureAwait(false);

            // For skills/spells/abilities topics, append global catalog after the command entry.
            var lower = verb.ToLowerInvariant();
            if (lower == "skills")
                await AppendGlobalAbilityCatalogAsync(context.Output, AbilityKind.Skill).ConfigureAwait(false);
            else if (lower == "spells")
                await AppendGlobalAbilityCatalogAsync(context.Output, AbilityKind.Spell).ConfigureAwait(false);
            else if (lower == "abilities")
                await AppendGlobalAbilityCatalogAsync(context.Output, kind: null).ConfigureAwait(false);
        }

        private async Task TryShowAbilityHelpAsync(CommandContext context, string topic)
        {
            // Exact-match the ability registry by id (ability ids are lowercase).
            var lowerTopic = topic.ToLowerInvariant();

            if (_abilityRegistry.TryGet(lowerTopic, out var abilityDef))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    FormatAbilityHelp(abilityDef), OutputSeverity.System, OutputCategory.Help))
                    .ConfigureAwait(false);
                return;
            }

            // Also try prefix-matching against all ability ids and display names.
            AbilityDefinition? matched = null;
            foreach (var id in _abilityRegistry.AllIds)
            {
                if (!_abilityRegistry.TryGet(id, out var candidate)) continue;
                if (id.StartsWith(lowerTopic, StringComparison.OrdinalIgnoreCase)
                    || candidate.Name.StartsWith(topic, StringComparison.OrdinalIgnoreCase))
                {
                    if (matched != null)
                    {
                        // Ambiguous — just say no help found rather than guessing.
                        matched = null;
                        break;
                    }
                    matched = candidate;
                }
            }

            if (matched != null)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    FormatAbilityHelp(matched), OutputSeverity.System, OutputCategory.Help))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(
                new PlainMessage($"No help found for '{topic}'.", OutputSeverity.System, OutputCategory.Help))
                .ConfigureAwait(false);
        }

        private async Task AppendGlobalAbilityCatalogAsync(IOutputWriter output, AbilityKind? kind)
        {
            await output.WriteAsync(new PlainMessage(
                kind.HasValue
                    ? $"\nAll registered {kind.Value.ToString().ToLower()}s:"
                    : "\nAll registered abilities:",
                OutputSeverity.System, OutputCategory.Help)).ConfigureAwait(false);

            foreach (var id in _abilityRegistry.AllIds.OrderBy(x => x))
            {
                if (!_abilityRegistry.TryGet(id, out var def)) continue;
                if (kind.HasValue && def.Kind != kind.Value) continue;

                var costStr = def.Costs.Count == 0
                    ? "no cost"
                    : string.Join(", ", def.Costs.Select(c => $"{c.Amount} {c.Resource.ToString().ToLower()}"));
                var cdStr = def.CooldownSeconds > 0f ? $"{def.CooldownSeconds:F0}s" : "none";
                var line = $"  {def.Id,-20} — {def.Name}. ({def.Kind}, {def.Activation}, {def.Targeting}, cost: {costStr}, cd: {cdStr})";
                await output.WriteAsync(new PlainMessage(line, OutputSeverity.System, OutputCategory.Help)).ConfigureAwait(false);
            }
        }

        private static string FormatAbilityHelp(AbilityDefinition def)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ability:    {def.Name} ({def.Id})");
            sb.AppendLine($"Kind:       {def.Kind} | Activation: {def.Activation} | Targeting: {def.Targeting}");

            if (def.Costs.Count > 0)
            {
                var costStr = string.Join(", ", def.Costs.Select(c => $"{c.Amount} {c.Resource.ToString().ToLower()}"));
                sb.AppendLine($"Cost:       {costStr}");
            }
            else
            {
                sb.AppendLine("Cost:       none");
            }

            if (def.CooldownSeconds > 0f)
                sb.AppendLine($"Cooldown:   {def.CooldownSeconds:F0}s");
            else
                sb.AppendLine("Cooldown:   none");

            if (def.Kind == AbilityKind.Skill && def.Activation == Activation.Active)
                sb.AppendLine($"Invocation: type '{def.Id}' directly (or prefix-match, e.g. '{def.Id.Substring(0, Math.Min(2, def.Id.Length))}...')");
            else if (def.Kind == AbilityKind.Spell && def.Activation == Activation.Active)
                sb.AppendLine($"Invocation: cast {def.Id} (or prefix-match: c {def.Id.Substring(0, Math.Min(3, def.Id.Length))}...)");
            else if (def.Activation == Activation.Passive)
                sb.AppendLine("Invocation: passive — applies automatically when learned");
            else if (def.Activation == Activation.Triggered)
                sb.AppendLine("Invocation: triggered — activates automatically on a condition");

            return sb.ToString().TrimEnd();
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
