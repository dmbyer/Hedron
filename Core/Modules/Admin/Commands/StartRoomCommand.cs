using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin command <c>startroom [here]</c>. With no argument, shows the resolved starting room
    /// (read-only). With <c>here</c>, declares the invoker's current room as the world's starting
    /// room: writes <c>world.yaml</c> (the durable definition), re-resolves the live snapshot through
    /// <see cref="IWorldContentLoader.ResolveStartingRoom"/>, and publishes
    /// <see cref="StartingRoomSetByAdminEvent"/>. Mutates no persistent entity — existing characters'
    /// respawn points are untouched (<c>setrespawn</c> exists for that).
    /// </summary>
    public sealed class StartRoomCommand : ICommand
    {
        private const string HereToken = "here";

        private readonly EntityService _entityService;
        private readonly IWorldManifestStore _manifestStore;
        private readonly IWorldContentLoader _contentLoader;
        private readonly WorldConfiguration _worldConfig;
        private readonly IEventBus _eventBus;

        public string Name => "startroom";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Show or set the world's starting room.";
        public string LongDescription =>
            "Without an argument, shows the resolved starting room, where it came from, and any " +
            "declared-but-unresolved id. 'startroom here' declares the room you are standing in as " +
            "the starting room in world.yaml and applies it live. Existing characters' respawn " +
            "points are not changed.";
        public string Usage => "startroom [here]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("mode", typeof(string), CommandArgumentKind.Token,
                Required: false, "'here' to declare your current room; omit to inspect."),
        });

        public StartRoomCommand(
            EntityService entityService,
            IWorldManifestStore manifestStore,
            IWorldContentLoader contentLoader,
            WorldConfiguration worldConfig,
            IEventBus eventBus)
        {
            _entityService = entityService;
            _manifestStore = manifestStore;
            _contentLoader = contentLoader;
            _worldConfig = worldConfig;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            context.Args.TryGet<string>("mode", out var mode);

            if (string.IsNullOrWhiteSpace(mode))
            {
                await ShowAsync(context).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(mode.Trim(), HereToken, StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(context, $"Usage: {Usage}", OutputSeverity.Error).ConfigureAwait(false);
                return;
            }

            await SetHereAsync(context).ConfigureAwait(false);
        }

        private async Task ShowAsync(CommandContext context)
        {
            var start = _worldConfig.StartingRoom;
            var name = _entityService.TryGet<RoomComponent>(start.EntityId, out var room) ? room.Name : "(none)";
            var text = $"Starting room: {name} [{start.BlueprintId ?? "unresolved"}]\nSource: {DescribeSource(start.Source)}";
            if (!string.IsNullOrEmpty(start.DeclaredId) && start.Source != StartingRoomSource.Manifest)
                text += $"\nDeclared in world.yaml but not resolved: {start.DeclaredId}";
            await WriteAsync(context, text, OutputSeverity.System).ConfigureAwait(false);
        }

        private async Task SetHereAsync(CommandContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location) ||
                !_entityService.HasComponent<RoomComponent>(location.RoomEntityId) ||
                !_entityService.TryGet<BlueprintComponent>(location.RoomEntityId, out var bp) ||
                string.IsNullOrEmpty(bp.BlueprintId))
            {
                await WriteAsync(context, "You must be standing in a world room to declare it the starting room.",
                    OutputSeverity.Error).ConfigureAwait(false);
                return;
            }

            var previous = _worldConfig.StartingRoom.BlueprintId;
            var update = await _manifestStore
                .UpdateAsync(m => m with { StartingRoomId = bp.BlueprintId })
                .ConfigureAwait(false);
            if (!update.Success)
            {
                await WriteAsync(context,
                    $"world.yaml is malformed and was not changed; fix it by hand first. ({update.Error})",
                    OutputSeverity.Error).ConfigureAwait(false);
                return;
            }

            // Durable definition first, then the live derived state (INV-21 analogue).
            var resolved = _contentLoader.ResolveStartingRoom();

            await _eventBus.PublishAsync(new StartingRoomSetByAdminEvent(
                context.InvokerEntityId, resolved.EntityId, bp.BlueprintId, previous)).ConfigureAwait(false);

            await WriteAsync(context,
                $"Starting room set to '{bp.BlueprintId}' in world.yaml. New characters start here; " +
                "existing characters' respawn points are unchanged.",
                OutputSeverity.Confirmation).ConfigureAwait(false);
        }

        private static string DescribeSource(StartingRoomSource source) => source switch
        {
            StartingRoomSource.Manifest => "world.yaml",
            StartingRoomSource.Configuration => "World:StartingRoomBlueprintId (config fallback)",
            StartingRoomSource.VoidFallback => "void-room fallback",
            StartingRoomSource.AnyRoomFallback => "any-room fallback",
            _ => "unresolved (no rooms)",
        };

        private static Task WriteAsync(CommandContext context, string text, OutputSeverity severity) =>
            context.Output.WriteAsync(new PlainMessage(text, severity, OutputCategory.System));
    }
}
