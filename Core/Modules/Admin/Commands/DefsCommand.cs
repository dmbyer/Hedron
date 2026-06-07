using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin command <c>defs &lt;family&gt; [id]</c>.
    /// Generic inspector over every definition registry. Lists all ids in a family or
    /// dumps a single definition by id. Families: aspect, ability, effect, score.
    /// </summary>
    public sealed class DefsCommand : ICommand
    {
        private readonly IAspectRegistry _aspectRegistry;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IStatRegistry _statRegistry;

        public string Name => "defs";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Inspect definition registries.";
        public string LongDescription =>
            "Lists all ids in a definition family, or dumps a single definition by id.\n" +
            "Families: aspect, ability, effect, score.";
        public string Usage => "defs <family> [id]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("family", typeof(string), CommandArgumentKind.Token,
                Required: true, "Registry family name (aspect, ability, effect, score)."),
            new CommandArgument("id", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Definition id to inspect (omit to list all)."),
        });

        public DefsCommand(
            IAspectRegistry aspectRegistry,
            IAbilityRegistry abilityRegistry,
            IEffectRegistry effectRegistry,
            IStatRegistry statRegistry)
        {
            _aspectRegistry = aspectRegistry;
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
            _statRegistry = statRegistry;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var family = context.Args.Get<string>("family").Trim().ToLowerInvariant();
            context.Args.TryGet<string>("id", out var idArg);
            var id = string.IsNullOrWhiteSpace(idArg) ? null : idArg.Trim();

            switch (family)
            {
                case "aspect":
                    await HandleAspect(context, id).ConfigureAwait(false);
                    return;
                case "ability":
                    await HandleAbility(context, id).ConfigureAwait(false);
                    return;
                case "effect":
                    await HandleEffect(context, id).ConfigureAwait(false);
                    return;
                case "score":
                    await HandleScore(context, id).ConfigureAwait(false);
                    return;
                default:
                    await context.Output.WriteAsync(new PlainMessage(
                        $"Unknown family '{family}'. Known families: aspect, ability, effect, score.",
                        OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                    return;
            }
        }

        // -----------------------------------------------------------------------

        private async Task HandleAspect(CommandContext context, string? id)
        {
            if (id == null)
            {
                var sb = new StringBuilder("Aspects:");
                foreach (var key in _aspectRegistry.AllIds)
                    sb.Append($"\n  {key}");
                await context.Output.WriteAsync(
                    new PlainMessage(sb.ToString(), OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse<AspectId>(id, ignoreCase: true, out var aspectId) ||
                !_aspectRegistry.TryGet(aspectId, out var def))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Aspect '{id}' not found.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                $"Aspect: {def.Id}\n  Name: {def.Name}\n  Category: {def.Category}\n  Description: {def.Description}",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }

        private async Task HandleAbility(CommandContext context, string? id)
        {
            if (id == null)
            {
                var sb = new StringBuilder("Abilities:");
                foreach (var key in _abilityRegistry.AllIds)
                    sb.Append($"\n  {key}");
                await context.Output.WriteAsync(
                    new PlainMessage(sb.ToString(), OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_abilityRegistry.TryGet(id, out var def))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Ability '{id}' not found.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var effects = string.Join(", ", def.Effects);
            var aspect = def.Aspect is { IsEmpty: false } ? def.Aspect.ToString() : "(none)";
            await context.Output.WriteAsync(new PlainMessage(
                $"Ability: {def.Id}\n  Name: {def.Name}\n  Kind: {def.Kind}  Activation: {def.Activation}  Targeting: {def.Targeting}\n  Cooldown: {def.CooldownSeconds}s\n  Effects: {effects}\n  Aspect: {aspect}",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }

        private async Task HandleEffect(CommandContext context, string? id)
        {
            if (id == null)
            {
                var sb = new StringBuilder("Effects:");
                foreach (var key in _effectRegistry.AllIds)
                    sb.Append($"\n  {key}");
                await context.Output.WriteAsync(
                    new PlainMessage(sb.ToString(), OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!_effectRegistry.TryGet(id, out var def))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Effect '{id}' not found.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage(
                $"Effect: {def.EffectId}\n  Kind: {def.Kind}  Category: {def.Category}  Phase: {def.Phase}\n  Target: {def.Params.TargetScore}  Magnitude: {def.Params.BaseMagnitude}\n  Duration: {def.Duration}s  Stacking: {def.Stacking}",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }

        private async Task HandleScore(CommandContext context, string? id)
        {
            if (id == null)
            {
                var sb = new StringBuilder("Scores:");
                foreach (var key in _statRegistry.AllIds)
                    sb.Append($"\n  {key}");
                await context.Output.WriteAsync(
                    new PlainMessage(sb.ToString(), OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse<Hedron.Core.Modules.Stats.ScoreId>(id, ignoreCase: true, out var scoreId) ||
                !_statRegistry.TryGet(scoreId, out var def))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Score '{id}' not found.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var gov = def.GoverningAttribute.HasValue ? def.GoverningAttribute.Value.ToString() : "(none)";
            await context.Output.WriteAsync(new PlainMessage(
                $"Score: {def.ScoreId}\n  Role: {def.Role}  GoverningAttribute: {gov}",
                OutputSeverity.System, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
