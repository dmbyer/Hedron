using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Tests.Help
{
    /// <summary>A metadata-only command for help lookup tests.</summary>
    internal sealed class FakeCommand : ICommand
    {
        public FakeCommand(string name, bool adminOnly = false, params string[] aliases)
        {
            Name = name;
            Aliases = aliases;
            RequiredPrivileges = adminOnly
                ? new IAuthorizationRequirement[] { new AdminRequirement() }
                : Array.Empty<IAuthorizationRequirement>();
            Category = adminOnly ? CommandCategory.Admin : CommandCategory.Player;
        }

        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }
        public CommandCategory Category { get; }
        public string ShortDescription => $"{Name} short";
        public string LongDescription => $"{Name} long";
        public string Usage => Name;
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; }
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;
        public CommandMatchingMode MatchingMode => RequiredPrivileges.Count > 0 ? CommandMatchingMode.Full : CommandMatchingMode.Partial;
        public Task ExecuteAsync(CommandContext context) => Task.CompletedTask;
    }

    /// <summary>An <see cref="IVerbRegistry"/> with the dispatcher's exact/prefix semantics over fixed commands.</summary>
    internal sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly List<ICommand> _commands;

        public FakeVerbRegistry(params ICommand[] commands) => _commands = commands.ToList();

        public IReadOnlyCollection<ICommand> AllCommands => _commands;

        public bool TryGetExact(string verb, out ICommand? command)
        {
            command = _commands.FirstOrDefault(c =>
                string.Equals(c.Name, verb, StringComparison.OrdinalIgnoreCase) ||
                c.Aliases.Any(a => string.Equals(a, verb, StringComparison.OrdinalIgnoreCase)));
            return command is not null;
        }

        public IReadOnlyList<ICommand> GetPrefixCandidates(string verb) => _commands
            .Where(c => c.MatchingMode == CommandMatchingMode.Partial && c.Name.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>An ability registry over chosen rows (for multi-word names and ambiguous prefixes).</summary>
    internal sealed class TestAbilityRegistry : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
    {
        public TestAbilityRegistry(params AbilityDefinition[] rows) : base(rows, d => d.Id) { }

        public static AbilityDefinition Ability(string id, string name) => new(
            id, name, AbilityKind.Spell, Activation.Active, Targeting.Target,
            Costs: new List<ResourceCost>(), Effects: new List<string>(), CooldownSeconds: 0f);
    }

    /// <summary>Admin iff the session is one of the given admin sessions.</summary>
    internal sealed class FakeAuthorization : IAuthorizationChecker
    {
        private readonly HashSet<ISession> _admins;

        public FakeAuthorization(params ISession[] admins) => _admins = admins.ToHashSet();

        public bool IsSatisfied(IAuthorizationRequirement requirement, ISession session) =>
            requirement is not AdminRequirement || _admins.Contains(session);
    }
}
