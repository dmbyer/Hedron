using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>Sub-actions of <see cref="SpawnRuleCommand"/>; omitted means list.</summary>
    public enum SpawnRuleAction
    {
        Add,
        Set,
        Remove,
    }

    /// <summary>
    /// Admin verb <c>spawnrule</c>: inspect and author the current room's spawn rules.
    /// <c>spawnrule</c> lists (read-only); <c>add &lt;blueprintId&gt; [count] [delaySeconds] [id]</c>,
    /// <c>set &lt;ruleId&gt; &lt;count|delay|blueprint&gt; &lt;value&gt;</c> and
    /// <c>remove &lt;ruleId&gt;</c> mutate.
    /// </summary>
    /// <remarks>
    /// Orchestration (Initiator, INV-8, INV-21): <see cref="IRoomBuilderSystem"/> validates and updates
    /// the template, the live <see cref="SpawnConfigComponent"/> and the spawn tracker; the command then
    /// writes the room YAML and publishes <see cref="SpawnRuleAuthoredByAdminEvent"/>. A refusal changes
    /// nothing and publishes nothing. Sub-action arity/type checks are hand-rolled here — the parser has
    /// no sub-action schema support (backlog: promote at the third verb of this shape).
    /// </remarks>
    public sealed class SpawnRuleCommand : ICommand
    {
        private const string AddUsage = "spawnrule add <blueprintId> [count] [delaySeconds] [id]";
        private const string SetUsage = "spawnrule set <ruleId> <count|delay|blueprint> <value>";
        private const string RemoveUsage = "spawnrule remove <ruleId>";

        private readonly EntityService _entityService;
        private readonly IRoomBuilderSystem _roomBuilder;
        private readonly ISpawnSystem _spawnSystem;
        private readonly SpawnRuleAuthoringOutcome _outcome;
        private readonly IClock _clock;

        public string Name => "spawnrule";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Show or author the current room's spawn rules.";
        public string LongDescription =>
            "Without arguments, lists this room's spawn rules: id, blueprint, live/count, respawn delay " +
            "and next respawn. 'add <blueprintId> [count] [delaySeconds] [id]' adds a rule (count " +
            $"1-{SpawnRuleLimits.MaxCountPerRule}, default 1; delay default {SpawnRuleLimits.DefaultRespawnDelaySeconds}s; " +
            "id minted when omitted) and spawns it now. 'set <ruleId> <count|delay|blueprint> <value>' " +
            "changes one field; shrinking a count detaches surplus instances (they live until they die " +
            "or the world reloads). 'remove <ruleId>' deletes the rule. Changes apply live and are " +
            "written to the room's YAML.";
        public string Usage => "spawnrule [add|set|remove ...]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("action", typeof(SpawnRuleAction), CommandArgumentKind.Token,
                Required: false, "add, set or remove; omit to list."),
            new CommandArgument("arg1", typeof(string), CommandArgumentKind.Token, Required: false, "Sub-action argument."),
            new CommandArgument("arg2", typeof(string), CommandArgumentKind.Token, Required: false, "Sub-action argument."),
            new CommandArgument("arg3", typeof(string), CommandArgumentKind.Token, Required: false, "Sub-action argument."),
            new CommandArgument("arg4", typeof(string), CommandArgumentKind.Token, Required: false, "Sub-action argument."),
        });

        public SpawnRuleCommand(
            EntityService entityService,
            IRoomBuilderSystem roomBuilder,
            ISpawnSystem spawnSystem,
            SpawnRuleAuthoringOutcome outcome,
            IClock clock)
        {
            _entityService = entityService;
            _roomBuilder = roomBuilder;
            _spawnSystem = spawnSystem;
            _outcome = outcome;
            _clock = clock;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var roomId = _entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location)
                ? location.RoomEntityId
                : 0u;

            if (!context.Args.TryGet<SpawnRuleAction>("action", out var action))
            {
                await ListAsync(context, roomId).ConfigureAwait(false);
                return;
            }

            context.Args.TryGet<string>("arg1", out var arg1);
            context.Args.TryGet<string>("arg2", out var arg2);
            context.Args.TryGet<string>("arg3", out var arg3);
            context.Args.TryGet<string>("arg4", out var arg4);

            switch (action)
            {
                case SpawnRuleAction.Add:
                {
                    var count = 1;
                    var delay = SpawnRuleLimits.DefaultRespawnDelaySeconds;
                    if (string.IsNullOrEmpty(arg1) ||
                        (arg2 is not null && !int.TryParse(arg2, out count)) ||
                        (arg3 is not null && !int.TryParse(arg3, out delay)))
                    {
                        await WriteAsync(context, $"Usage: {AddUsage}", OutputSeverity.Error).ConfigureAwait(false);
                        return;
                    }
                    var result = _roomBuilder.AddSpawnRule(roomId, arg1, count, delay, arg4);
                    await CompleteAsync(context, roomId, result, SpawnRuleChange.Added).ConfigureAwait(false);
                    return;
                }

                case SpawnRuleAction.Set:
                {
                    if (string.IsNullOrEmpty(arg1) || string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                    {
                        await WriteAsync(context, $"Usage: {SetUsage}", OutputSeverity.Error).ConfigureAwait(false);
                        return;
                    }
                    SpawnRuleUpdate? update = arg2.ToLowerInvariant() switch
                    {
                        "count" when int.TryParse(arg3, out var c) => new SpawnRuleUpdate(Count: c),
                        "delay" when int.TryParse(arg3, out var d) => new SpawnRuleUpdate(RespawnDelaySeconds: d),
                        "blueprint" => new SpawnRuleUpdate(BlueprintId: arg3),
                        _ => null,
                    };
                    if (update is null)
                    {
                        await WriteAsync(context, $"Usage: {SetUsage}", OutputSeverity.Error).ConfigureAwait(false);
                        return;
                    }
                    var result = _roomBuilder.UpdateSpawnRule(roomId, arg1, update);
                    await CompleteAsync(context, roomId, result, SpawnRuleChange.Updated).ConfigureAwait(false);
                    return;
                }

                case SpawnRuleAction.Remove:
                {
                    if (string.IsNullOrEmpty(arg1))
                    {
                        await WriteAsync(context, $"Usage: {RemoveUsage}", OutputSeverity.Error).ConfigureAwait(false);
                        return;
                    }
                    var result = _roomBuilder.RemoveSpawnRule(roomId, arg1);
                    await CompleteAsync(context, roomId, result, SpawnRuleChange.Removed).ConfigureAwait(false);
                    return;
                }
            }
        }

        private async Task CompleteAsync(
            CommandContext context, uint roomId, SpawnRuleMutationResult result, SpawnRuleChange change)
        {
            if (!result.Success || result.Rule is null)
            {
                await WriteAsync(context, result.Error ?? "Spawn rule change refused.", OutputSeverity.Error).ConfigureAwait(false);
                return;
            }

            await _outcome.CompleteAsync(context.InvokerEntityId, roomId, result, change).ConfigureAwait(false);

            var rule = result.Rule;

            var verb = change switch
            {
                SpawnRuleChange.Added => "added",
                SpawnRuleChange.Updated => "updated",
                _ => "removed",
            };
            await WriteAsync(context,
                $"Spawn rule '{rule.Id}' {verb} in room '{result.RoomBlueprintId}': {rule.BlueprintId} x{rule.Count}, " +
                $"respawn {rule.RespawnDelaySeconds}s. Spawned {result.Sync.SpawnedEntityIds.Count}, " +
                $"detached {result.Sync.SlotsDetached}.",
                OutputSeverity.Confirmation).ConfigureAwait(false);
        }

        private async Task ListAsync(CommandContext context, uint roomId)
        {
            if (!_entityService.HasComponent<RoomComponent>(roomId) ||
                !_entityService.TryGet<BlueprintComponent>(roomId, out var bp))
            {
                await WriteAsync(context, "You must be standing in a world room.", OutputSeverity.Error).ConfigureAwait(false);
                return;
            }

            var statuses = _spawnSystem.DescribeRoom(roomId);
            if (statuses.Count == 0)
            {
                await WriteAsync(context, $"Room '{bp.BlueprintId}' has no spawn rules.", OutputSeverity.System).ConfigureAwait(false);
                return;
            }

            var now = _clock.UtcNow;
            var text = new StringBuilder($"Spawn rules in room '{bp.BlueprintId}':");
            foreach (var s in statuses)
            {
                var next = s.NextRespawnAt is { } at
                    ? $"next in {Math.Max(0, (int)Math.Ceiling((at - now).TotalSeconds))}s"
                    : "none pending";
                text.Append($"\n  {s.RuleId}: {s.BlueprintId} {s.Live}/{s.Count} live, respawn {s.RespawnDelaySeconds}s, {next}");
            }
            await WriteAsync(context, text.ToString(), OutputSeverity.System).ConfigureAwait(false);
        }

        private static Task WriteAsync(CommandContext context, string text, OutputSeverity severity) =>
            context.Output.WriteAsync(new PlainMessage(text, severity, OutputCategory.System));
    }
}
