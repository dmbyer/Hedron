using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Help.Commands
{
    /// <summary>
    /// <c>help</c> — with no argument, the visible command index plus the Topics block; with an
    /// argument, the first match among verbs, authored help topics and abilities. The lookup rule
    /// (exact before prefix, verb → topic → ability, visibility-gated) lives in
    /// <see cref="IHelpLookupSystem"/>; this initiator gathers the query, calls it, and renders the
    /// result (INV-8). Read-only: publishes nothing.
    /// <para>
    /// Administrators also get an origin footer after a topic (<see cref="HelpTopicOriginMessage"/>),
    /// written as a separate message so player output never carries it.
    /// </para>
    /// </summary>
    public sealed class HelpCommand : ICommand
    {
        private static readonly AdminRequirement Admin = new();

        private readonly IHelpLookupSystem _lookup;
        private readonly IAuthorizationChecker _authorizationChecker;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "help";
        public IReadOnlyList<string> Aliases { get; } = new[] { "?" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "Show command help and help topics.";
        public string LongDescription =>
            "With no argument, lists all commands available to you grouped by category, followed by " +
            "the help topics. With an argument, shows help for a command, a topic or an ability. " +
            "Partial names are accepted: 'help lo' displays help for 'look'. Topics may be typed " +
            "with spaces or hyphens: 'help getting started'. If no command or topic matches, the " +
            "ability registry is searched: 'help kick' shows kick's details. Typing 'help skills', " +
            "'help spells', or 'help abilities' also appends a global catalog.";
        public string Usage => "help [<command | topic | ability>]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("topic", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Command, topic or ability to look up."),
        });

        public HelpCommand(
            IHelpLookupSystem lookup,
            IAuthorizationChecker authorizationChecker,
            IAbilityRegistry abilityRegistry)
        {
            _lookup = lookup;
            _authorizationChecker = authorizationChecker;
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("topic", out var raw);
            var query = HelpQuery.Parse(raw);

            switch (_lookup.Resolve(query, context.Session))
            {
                case HelpResolution.Index:
                    await WriteIndexAsync(context).ConfigureAwait(false);
                    break;

                case HelpResolution.Verb verb:
                    await context.Output.WriteAsync(new HelpEntryMessage(
                        verb.Help.Name, verb.Help.LongDescription, verb.Help.Usage, verb.Help.Aliases)).ConfigureAwait(false);
                    if (verb.Catalog is not null)
                        await AppendGlobalAbilityCatalogAsync(context.Output, verb.Catalog.Kind).ConfigureAwait(false);
                    break;

                case HelpResolution.Topic topic:
                    await context.Output.WriteAsync(HelpTopicRendering.ToMessage(topic.Help)).ConfigureAwait(false);
                    if (_authorizationChecker.IsSatisfied(Admin, context.Session))
                        await context.Output.WriteAsync(new HelpTopicOriginMessage(topic.Help.Origin)).ConfigureAwait(false);
                    break;

                case HelpResolution.Ability ability:
                    await context.Output.WriteAsync(new PlainMessage(
                        FormatAbilityHelp(ability.Definition), OutputSeverity.System, OutputCategory.Help)).ConfigureAwait(false);
                    break;

                case HelpResolution.Ambiguous ambiguous:
                    var kind = ambiguous.Family == HelpMatchFamily.Verb ? "command" : "topic";
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Ambiguous {kind} '{query.Raw}'. Did you mean: {string.Join(", ", ambiguous.Candidates)}?",
                        OutputSeverity.System, OutputCategory.Help)).ConfigureAwait(false);
                    break;

                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"No help found for '{query.Raw}'.", OutputSeverity.System, OutputCategory.Help)).ConfigureAwait(false);
                    break;
            }
        }

        private async Task WriteIndexAsync(CommandContext context)
        {
            var index = _lookup.BuildIndex(context.Session);
            var entries = index.Verbs
                .Select(v => new HelpIndexEntry(v.Name, v.ShortDescription, v.Category, v.Aliases))
                .ToList();
            await context.Output.WriteAsync(new HelpIndexMessage(entries)).ConfigureAwait(false);

            if (index.Topics.Count > 0)
            {
                await context.Output.WriteAsync(new HelpTopicIndexMessage(
                    index.Topics.Select(t => new HelpTopicIndexEntry(t.Id, t.Title)).ToList())).ConfigureAwait(false);
            }
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
    }
}
