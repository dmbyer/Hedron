using System;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Abilities.Commands
{
    /// <summary>
    /// Internal invocation service (NOT an <c>ICommand</c>) called by
    /// <see cref="Hedron.Core.Commands.CommandDispatcher"/> Phase 3 after the ability-verb
    /// resolver confirms a unique Active Skill match.
    /// <para>
    /// Delegates the full invocation pipeline to <see cref="AbilityInvocationPipeline"/>.
    /// </para>
    /// </summary>
    public sealed class SkillInvocationCommand
    {
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly AbilityInvocationPipeline _pipeline;
        private readonly ILogger<SkillInvocationCommand> _logger;

        public SkillInvocationCommand(
            IAbilityRegistry abilityRegistry,
            AbilityInvocationPipeline pipeline,
            ILogger<SkillInvocationCommand> logger)
        {
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(
            ISession session,
            uint actorId,
            string abilityId,
            string rawTail,
            IOutputWriter output)
        {
            // Lookup ability definition — registry miss is a bug, log it.
            if (!_abilityRegistry.TryGet(abilityId, out var def))
            {
                _logger.LogError(
                    "SkillInvocationCommand: ability '{AbilityId}' not found in registry for actor {ActorId}.",
                    abilityId, actorId);
                return;
            }

            await _pipeline.InvokeAsync(actorId, abilityId, def, rawTail, output, nameof(SkillInvocationCommand))
                .ConfigureAwait(false);
        }
    }
}
