using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Abilities.Commands
{
    // ─────────────────────────────────────────────────────────────────────────────
    // AbilitiesCommand — lists non-Skill, non-Spell known abilities (future kinds).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Player verb <c>abilities</c>. Lists known abilities that are neither Skill-kind nor
    /// Spell-kind (future ability kinds such as stances, racials, etc.). Directs the player
    /// to <c>skills</c> or <c>spells</c> when nothing is shown here.
    /// </summary>
    public sealed class AbilitiesCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "abilities";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List other known abilities (non-Skill, non-Spell).";
        public string LongDescription =>
            "Displays known abilities that are not classified as skills or spells — " +
            "future kinds such as stances, racials, and feats will appear here. " +
            "Use 'skills' to see your skills and 'spells' to see your spells.";
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

            var other = known
                .Where(id => _abilityRegistry.TryGet(id, out var d)
                             && d.Kind != AbilityKind.Skill
                             && d.Kind != AbilityKind.Spell)
                .ToList();

            bool hasSkillsOrSpells = known.Any(id =>
                _abilityRegistry.TryGet(id, out var d)
                && (d.Kind == AbilityKind.Skill || d.Kind == AbilityKind.Spell));

            if (other.Count == 0)
            {
                string msg = hasSkillsOrSpells
                    ? "You have no other abilities. Use 'skills' to see your skills and 'spells' to see your spells."
                    : "You have no abilities. Use 'skills' or 'spells' to see what can be learned.";
                await context.Output.WriteAsync(new PlainMessage(msg, OutputSeverity.System))
                    .ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage("Known abilities:", OutputSeverity.System))
                .ConfigureAwait(false);

            foreach (var id in other)
            {
                if (!_abilityRegistry.TryGet(id, out var def)) continue;
                var cooldown = _abilitySystem.GetCooldownRemaining(entityId, id);
                await context.Output.WriteAsync(new AbilityDisplayMessage(def, cooldown))
                    .ConfigureAwait(false);
            }

            await context.Output.WriteAsync(new PlainMessage(
                "Use 'skills' to see your skills and 'spells' to see your spells.",
                OutputSeverity.System)).ConfigureAwait(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SkillsCommand — lists known Skill-kind abilities.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Player verb <c>skills</c>. Lists all known Skill-kind abilities with their invocation
    /// verb, cooldown, and resource costs. Active Skills show the id as the invocation form
    /// (players type the id directly, or a prefix of it).
    /// </summary>
    public sealed class SkillsCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "skills";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List your known skills.";
        public string LongDescription =>
            "Displays all skills you have learned, including activation type, targeting, " +
            "resource costs, cooldown status, and the invocation verb. " +
            "Active skills are invoked by typing their id (or a unique prefix) directly. " +
            "Use 'spells' to see spells. Type 'help <skill-name>' to learn about any skill.";
        public string Usage => "skills";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = CommandArgumentSchema.Empty;

        public SkillsCommand(IAbilitySystem abilitySystem, IAbilityRegistry abilityRegistry)
        {
            _abilitySystem = abilitySystem;
            _abilityRegistry = abilityRegistry;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;
            var known = _abilitySystem.GetKnown(entityId);

            var skills = known
                .Where(id => _abilityRegistry.TryGet(id, out var d) && d.Kind == AbilityKind.Skill)
                .ToList();

            if (skills.Count == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You know no skills. Use 'spells' to see your spells.",
                    OutputSeverity.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage("Known skills:", OutputSeverity.System))
                .ConfigureAwait(false);

            foreach (var id in skills)
            {
                if (!_abilityRegistry.TryGet(id, out var def)) continue;
                var cooldown = _abilitySystem.GetCooldownRemaining(entityId, id);

                // For Active skills, show invocation hint as prefix: "  [invoke: kick] ..."
                string invocationHint = def.Activation == Activation.Active
                    ? $"[invoke: {def.Id}] "
                    : string.Empty;

                var baseLine = new AbilityDisplayMessage(def, cooldown).Format();
                await context.Output.WriteAsync(new PlainMessage(invocationHint + baseLine, OutputSeverity.System))
                    .ConfigureAwait(false);
            }

            await context.Output.WriteAsync(new PlainMessage(
                "Use 'spells' to see your spells. Type 'help <skill-name>' to learn about any skill.",
                OutputSeverity.System)).ConfigureAwait(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SpellsCommand — lists known Spell-kind abilities.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Player verb <c>spells</c>. Lists all known Spell-kind abilities with their invocation
    /// form (<c>cast &lt;name&gt;</c>), cooldown, and resource costs.
    /// </summary>
    public sealed class SpellsCommand : ICommand
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public string Name => "spells";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "List your known spells.";
        public string LongDescription =>
            "Displays all spells you have learned, including activation type, targeting, " +
            "resource costs, cooldown status, and the invocation form. " +
            "Active spells are invoked with 'cast <spell-name>'. " +
            "Use 'skills' to see skills. Type 'help <spell-name>' to learn about any spell.";
        public string Usage => "spells";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = CommandArgumentSchema.Empty;

        public SpellsCommand(IAbilitySystem abilitySystem, IAbilityRegistry abilityRegistry)
        {
            _abilitySystem = abilitySystem;
            _abilityRegistry = abilityRegistry;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;
            var known = _abilitySystem.GetKnown(entityId);

            var spells = known
                .Where(id => _abilityRegistry.TryGet(id, out var d) && d.Kind == AbilityKind.Spell)
                .ToList();

            if (spells.Count == 0)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You know no spells. Use 'skills' to see your skills.",
                    OutputSeverity.System)).ConfigureAwait(false);
                return;
            }

            await context.Output.WriteAsync(new PlainMessage("Known spells:", OutputSeverity.System))
                .ConfigureAwait(false);

            foreach (var id in spells)
            {
                if (!_abilityRegistry.TryGet(id, out var def)) continue;
                var cooldown = _abilitySystem.GetCooldownRemaining(entityId, id);

                // For Active spells, show invocation form: "  [invoke: cast empower] ..."
                string invocationHint = def.Activation == Activation.Active
                    ? $"[invoke: cast {def.Id}] "
                    : string.Empty;

                var baseLine = new AbilityDisplayMessage(def, cooldown).Format();
                await context.Output.WriteAsync(new PlainMessage(invocationHint + baseLine, OutputSeverity.System))
                    .ConfigureAwait(false);
            }

            await context.Output.WriteAsync(new PlainMessage(
                "Use 'skills' to see your skills. Type 'help <spell-name>' to learn about any spell.",
                OutputSeverity.System)).ConfigureAwait(false);
        }
    }
}
