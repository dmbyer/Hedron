using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Commands.Events;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Abilities.Commands;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Runtime of the command-Initiator tier. Builds a verb → command map at construction,
    /// then for each dispatch performs a two-phase verb lookup — exact match first, then prefix
    /// resolution for commands whose <see cref="CommandMatchingMode"/> is
    /// <see cref="CommandMatchingMode.Partial"/>. Implements <see cref="IVerbRegistry"/> to
    /// expose a read-only view of the command namespace to <c>HelpCommand</c> and future
    /// tab-completion without coupling those consumers to the full dispatcher contract.
    /// </summary>
    public class CommandDispatcher : ICommandDispatcher, IVerbRegistry
    {
        // Exact-match map: primary names + all aliases → command.
        private readonly Dictionary<string, ICommand> _byVerb =
            new(StringComparer.OrdinalIgnoreCase);

        // One entry per command (not per alias) — used by prefix resolution and AllCommands.
        private readonly List<ICommand> _allCommands = new();

        private readonly IAuthorizationChecker _authorizationChecker;
        private readonly ICommandArgumentParser _argumentParser;
        private readonly IOutputWriterFactory _outputWriterFactory;
        private readonly IEventBus _eventBus;
        private readonly IEntityStateService _entityStateService;
        private readonly ILogger<CommandDispatcher> _logger;
        private readonly IServiceProvider _services;
        private readonly IAbilityVerbResolver _abilityVerbResolver;
        private readonly SkillInvocationCommand _skillInvocationCommand;

        public CommandDispatcher(
            IEnumerable<ICommand> commands,
            IAuthorizationChecker authorizationChecker,
            ICommandArgumentParser argumentParser,
            IOutputWriterFactory outputWriterFactory,
            IEventBus eventBus,
            IEntityStateService entityStateService,
            ILogger<CommandDispatcher> logger,
            IServiceProvider services,
            IAbilityVerbResolver abilityVerbResolver,
            SkillInvocationCommand skillInvocationCommand)
        {
            if (commands is null) throw new ArgumentNullException(nameof(commands));
            _authorizationChecker = authorizationChecker ?? throw new ArgumentNullException(nameof(authorizationChecker));
            _argumentParser = argumentParser ?? throw new ArgumentNullException(nameof(argumentParser));
            _outputWriterFactory = outputWriterFactory ?? throw new ArgumentNullException(nameof(outputWriterFactory));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _entityStateService = entityStateService ?? throw new ArgumentNullException(nameof(entityStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _abilityVerbResolver = abilityVerbResolver ?? throw new ArgumentNullException(nameof(abilityVerbResolver));
            _skillInvocationCommand = skillInvocationCommand ?? throw new ArgumentNullException(nameof(skillInvocationCommand));

            foreach (var command in commands)
            {
                _allCommands.Add(command);
                Register(command.Name, command);
                foreach (var alias in command.Aliases)
                    Register(alias, command);
            }
        }

        // --- IVerbRegistry -----------------------------------------------------------

        public IReadOnlyCollection<ICommand> AllCommands => _allCommands;

        public bool TryGetExact(string verb, out ICommand? command)
            => _byVerb.TryGetValue(verb, out command);

        public IReadOnlyList<ICommand> GetPrefixCandidates(string verb)
            => _allCommands
                .Where(c => c.MatchingMode == CommandMatchingMode.Partial
                            && c.Name.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // --- ICommandDispatcher ------------------------------------------------------

        public async Task DispatchAsync(ISession session, string input)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(input)) return;
            if (session.PlayerEntityId == 0) return;

            var output = _outputWriterFactory.Create(session);
            var trimmed = input.Trim();
            var splitAt = trimmed.IndexOf(' ');
            var verb = splitAt < 0 ? trimmed : trimmed[..splitAt];
            var rawTail = splitAt < 0 ? string.Empty : trimmed[(splitAt + 1)..].TrimStart();

            // Phase 1: exact lookup (primary names + aliases).
            if (!_byVerb.TryGetValue(verb, out var command))
            {
                // Phase 2: prefix resolution — delegates to IVerbRegistry so the filter logic
                // is not duplicated between the dispatcher and HelpCommand.
                var candidates = GetPrefixCandidates(verb);

                switch (candidates.Count)
                {
                    case 0:
                        // Phase 3: ability-verb fallback — runs only after both command phases miss.
                        // TryResolve returns false for ambiguous (>1) or zero match.
                        if (_abilityVerbResolver.TryResolve(session.PlayerEntityId, verb, out var resolvedAbilityId))
                        {
                            // Unique skill verb matched — route to the internal skill invocation pipeline.
                            await _skillInvocationCommand.InvokeAsync(
                                session, session.PlayerEntityId, resolvedAbilityId, rawTail, output)
                                .ConfigureAwait(false);
                            // Publish CommandExecutedEvent so audit/logging fires.
                            await PublishExecutedAsync(
                                session.PlayerEntityId, resolvedAbilityId, rawTail, CommandOutcome.Success)
                                .ConfigureAwait(false);
                            return;
                        }

                        // Check for ambiguous ability prefix — TryResolve returns false but we still want a disambiguation line.
                        var abilityCandidates = _abilityVerbResolver.GetInvocableVerbs(session.PlayerEntityId)
                            .Where(id => id.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (abilityCandidates.Count > 1)
                        {
                            await output.WriteAsync(new PlainMessage(
                                $"Ambiguous skill '{verb}'. Did you mean: {string.Join(", ", abilityCandidates)}?",
                                OutputSeverity.Error)).ConfigureAwait(false);
                            await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.ParseFailed)
                                .ConfigureAwait(false);
                            return;
                        }

                        // Zero match — fall through to unknown command.
                        await output.WriteAsync(new PlainMessage(
                            $"Unknown command: {verb}. Type 'help' for a list.", OutputSeverity.Error))
                            .ConfigureAwait(false);
                        await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.ParseFailed)
                            .ConfigureAwait(false);
                        return;

                    case 1:
                        command = candidates[0];
                        break;

                    default:
                        var names = string.Join(", ", candidates.Select(c => c.Name));
                        await output.WriteAsync(new PlainMessage(
                            $"Ambiguous command '{verb}'. Did you mean: {names}?", OutputSeverity.Error))
                            .ConfigureAwait(false);
                        await PublishExecutedAsync(session.PlayerEntityId, verb, string.Empty, CommandOutcome.ParseFailed)
                            .ConfigureAwait(false);
                        return;
                }
            }

            // Use the canonical name for all downstream operations so log lines are stable.
            var canonicalVerb = command.Name;

            // Incapacitation gate — checked before privilege so a blocked-incapacitated player
            // does not receive a misleading "not authorized" message for commands they would
            // normally be allowed to use.
            if (!command.UsableWhileIncapacitated
                && _entityStateService.IsInState(session.PlayerEntityId, EntityStateFlags.Incapacitated))
            {
                await output.WriteAsync(new PlainMessage(
                    "You are incapacitated and cannot do that.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, canonicalVerb, string.Empty, CommandOutcome.Refused)
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
                    await PublishExecutedAsync(session.PlayerEntityId, canonicalVerb, string.Empty, CommandOutcome.Unauthorized)
                        .ConfigureAwait(false);
                    return;
                }
            }

            // Argument parse
            var resolverCtx = new CommandArgumentResolverContext(session, session.PlayerEntityId, _services);
            var parseResult = _argumentParser.Parse(command.ArgumentSchema, rawTail, resolverCtx);
            if (parseResult is ParseResult.Failure failure)
            {
                await output.WriteAsync(new PlainMessage(
                    $"{failure.Reason} Type 'help {canonicalVerb}' for usage.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, canonicalVerb, string.Empty, CommandOutcome.ParseFailed)
                    .ConfigureAwait(false);
                return;
            }

            var parsedArgs = ((ParseResult.Success)parseResult).Args;
            var argsSummary = BuildArgsSummary(rawTail);
            var context = new CommandContext(session, session.PlayerEntityId, parsedArgs, output, _services);

            try
            {
                await command.ExecuteAsync(context).ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, canonicalVerb, argsSummary, CommandOutcome.Success)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command {Verb} threw for entity {EntityId}", canonicalVerb, session.PlayerEntityId);
                await output.WriteAsync(new PlainMessage(
                    "Something went wrong. The error has been logged.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                await PublishExecutedAsync(session.PlayerEntityId, canonicalVerb, argsSummary, CommandOutcome.Threw)
                    .ConfigureAwait(false);
            }
        }

        // --- Helpers -----------------------------------------------------------------

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
