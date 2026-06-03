using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Abilities.Commands
{
    public sealed class AbilitiesCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "abilities";
        public IReadOnlyList<string> Aliases { get; } = new[] { "skills", "spells" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List your known abilities, skills, and spells.";
        public string LongDescription =>
            "Displays all abilities you have learned, including kind (Skill/Spell), activation type, " +
            "targeting, resource costs, and current cooldown status.";
        public string Usage => "abilities";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = CommandArgumentSchema.Empty;

        public AbilitiesCommand(IAbilitySystem abilitySystem, IAbilityRegistry abilityRegistry)
        {
            _abilitySystem = abilitySystem;
            _abilityRegistry = abilityRegistry;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;
            var known = _abilitySystem.GetKnown(entityId);

            if (known.Count == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You know no abilities.",
                    OutputSeverity.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                "Known abilities:",
                OutputSeverity.System)).ConfigureAwait(false);

            foreach (var id in known)
            {
                if (!_abilityRegistry.TryGet(id, out var def))
                    continue;

                var cooldownRemaining = _abilitySystem.GetCooldownRemaining(entityId, id);
                await context.Output.WriteAsync(new AbilityDisplayMessage(def, cooldownRemaining))
                    .ConfigureAwait(false);
            }
        }
    }
}
