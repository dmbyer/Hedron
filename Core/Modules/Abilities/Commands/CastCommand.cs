using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities.Resolvers;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Abilities.Commands
{
    /// <summary>
    /// Player verb <c>cast &lt;spell&gt; [target]</c> (alias <c>c</c>).
    /// Resolves the spell argument via <see cref="KnownSpellResolver"/> (prefix-matched against
    /// the invoker's known Active Spells), then delegates to <see cref="AbilityInvocationPipeline"/>
    /// for the full target-resolution → combat-entry → activate → event-publish → strike pipeline.
    /// </summary>
    public sealed class CastCommand : ICommand
    {
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly KnownSpellResolver _spellResolver;
        private readonly AbilityInvocationPipeline _pipeline;

        public string Name => "cast";
        public IReadOnlyList<string> Aliases { get; } = new[] { "c" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Cast a spell.";
        public string LongDescription =>
            "Invokes a known spell. 'cast <spell> [target]'. " +
            "Use 'spells' to see your known spells. " +
            "Prefix matching is supported: 'cast emp' resolves to 'empower' if unambiguous.";
        public string Usage => "cast <spell> [target]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();

        public CommandArgumentSchema ArgumentSchema { get; }

        public CastCommand(
            IAbilityRegistry abilityRegistry,
            KnownSpellResolver spellResolver,
            AbilityInvocationPipeline pipeline)
        {
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
            _spellResolver = spellResolver ?? throw new ArgumentNullException(nameof(spellResolver));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("spell", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or id of the spell to cast.", _spellResolver),
                new CommandArgument("target", typeof(string), CommandArgumentKind.RestOfLine,
                    Required: false, "Optional target name or keyword."),
            });
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            // 1. Retrieve resolved spell id from argument (KnownSpellResolver canonical value = ability id).
            if (!context.Args.TryGet<string>("spell", out var spellId) || string.IsNullOrWhiteSpace(spellId))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You don't know that spell.", OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            // 2. Verify in registry (should always succeed if resolver ran correctly).
            if (!_abilityRegistry.TryGet(spellId!, out var def))
                return; // should not happen

            // 3. Get optional target token.
            context.Args.TryGet<string>("target", out var targetToken);

            // 4. Delegate to the shared pipeline.
            await _pipeline.InvokeAsync(
                context.InvokerEntityId,
                spellId!,
                def,
                targetToken,
                context.Output,
                nameof(CastCommand))
                .ConfigureAwait(false);
        }
    }
}
