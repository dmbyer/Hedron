using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Effects.Commands
{
    public sealed class AffectsCommand : ICommand
    {
        private readonly IEffectSystem _effectSystem;

        public string Name => "affects";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List your active effects.";
        public string LongDescription => "Displays all effects currently active on you, including category, power, and remaining duration.";
        public string Usage => "affects";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } = Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = CommandArgumentSchema.Empty;

        public AffectsCommand(IEffectSystem effectSystem)
        {
            _effectSystem = effectSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var effects = _effectSystem.GetActive(context.InvokerEntityId);

            if (effects.Count == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You have no active effects.",
                    OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                "Active effects:",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);

            foreach (var effect in effects)
            {
                await context.Output.WriteAsync(new EffectDisplayMessage(effect))
                    .ConfigureAwait(false);
            }
        }
    }
}
