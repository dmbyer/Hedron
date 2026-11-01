using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Progression.Commands
{
    /// <summary>
    /// Player verb <c>consider &lt;mob&gt;</c> (alias <c>con</c>). Prints one descriptive line saying
    /// how dangerous a mob in the room is relative to the invoker — never a number, tier or band.
    /// Read-only and free: no state change, no round, resource or cooldown; usable in combat.
    ///
    /// <para>
    /// Targets mirror <c>kill</c>: Combat's room lookup (mobs only, prefix match) and its
    /// <see cref="ICombatSystem.CanBeAttacked"/> protection gate. The ratio → label rule is
    /// <see cref="IRelativePowerSystem"/>'s; this command owns only the label → phrase table
    /// (presentation). Publishes <see cref="MobConsideredEvent"/> only when a label is shown.
    /// </para>
    /// </summary>
    public sealed class ConsiderCommand : ICommand
    {
        private const string CreaturesOnly = "You can only size up creatures.";
        private const string NotHere = "You don't see that here.";

        private readonly IRelativePowerSystem _relativePower;
        private readonly ICombatSystem _combatSystem;
        private readonly IItemSystem _itemSystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "consider";
        public IReadOnlyList<string> Aliases { get; } = new[] { "con" };
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Judge how dangerous a creature is to you.";
        public string LongDescription =>
            "Sizes up a creature in your room and tells you, in a single descriptive line, how " +
            "dangerous a fight with it would be for you right now: from no challenge at all, through " +
            "an even fight, up to one that would almost certainly kill you. The reading compares the " +
            "creature's strength with yours, counting your gear, your improvements and any effects " +
            "on you, so it changes as you grow. It never shows numbers. Considering is free: it takes " +
            "no time and can be used in the middle of a fight.";
        public string Usage => "consider <target>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("target", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name or keyword of the creature to size up."),
        });

        public ConsiderCommand(
            IRelativePowerSystem relativePower,
            ICombatSystem combatSystem,
            IItemSystem itemSystem,
            EntityService entityService,
            IEventBus eventBus)
        {
            _relativePower = relativePower;
            _combatSystem = combatSystem;
            _itemSystem = itemSystem;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("target", out var target);
            target = target?.Trim() ?? string.Empty;
            var invoker = context.InvokerEntityId;

            if (target.Length == 0)
            {
                await Write(context, "Consider whom?").ConfigureAwait(false);
                return;
            }

            if (string.Equals(target, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, "me", StringComparison.OrdinalIgnoreCase))
            {
                await Write(context, CreaturesOnly).ConfigureAwait(false);
                return;
            }

            _entityService.TryGet<LocationComponent>(invoker, out var location);

            if (location == null || !_combatSystem.TryFindTargetInRoom(location.RoomEntityId, target, out var mobId))
            {
                var isItem = _itemSystem.TryFindItemInInventory(invoker, target, out _) ||
                             (location != null && _itemSystem.TryFindItemInRoom(location.RoomEntityId, target, out _));
                await Write(context, isItem ? CreaturesOnly : NotHere).ConfigureAwait(false);
                return;
            }

            var name = DisplayName(mobId);

            if (!_combatSystem.CanBeAttacked(mobId))
            {
                await Write(context, $"{name} is not something you can fight.").ConfigureAwait(false);
                return;
            }

            var reading = _relativePower.Relate(invoker, mobId);
            if (reading.Label is not { } label)
            {
                await Write(context, $"You can't tell how dangerous {name} is.").ConfigureAwait(false);
                return;
            }

            await Write(context, Phrase(label, name)).ConfigureAwait(false);

            var blueprintId = _entityService.TryGet<BlueprintComponent>(mobId, out var bp)
                ? bp.BlueprintId
                : string.Empty;
            await _eventBus.PublishAsync(new MobConsideredEvent(invoker, blueprintId, label)).ConfigureAwait(false);
        }

        /// <summary>Label → fixed phrase. Presentation only; the ratio → label rule is the read's.</summary>
        public static string Phrase(RelativePowerLabel label, string name) => label switch
        {
            RelativePowerLabel.Trivial => $"{name} would be no challenge at all.",
            RelativePowerLabel.Easy => $"{name} should be easy prey.",
            RelativePowerLabel.Even => $"{name} looks like an even fight.",
            RelativePowerLabel.Dangerous => $"{name} looks dangerous.",
            RelativePowerLabel.Deadly => $"{name} would almost certainly kill you.",
            _ => $"You can't tell how dangerous {name} is.",
        };

        /// <summary>The authored mob name with its first character upper-cased.</summary>
        private string DisplayName(uint mobId)
        {
            var name = _entityService.TryGet<MobDataComponent>(mobId, out var mob) ? mob.Name : string.Empty;
            if (string.IsNullOrEmpty(name)) return "That";
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static Task Write(CommandContext context, string text) =>
            context.Output.WriteAsync(new PlainMessage(text, OutputSeverity.System, OutputCategory.System));
    }
}
