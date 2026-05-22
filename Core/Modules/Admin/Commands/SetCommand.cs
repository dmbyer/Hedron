using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>set &lt;property&gt; &lt;value&gt;</c>.
    /// Sets <c>name</c> or <c>description</c> on the administrator's current room.
    /// </summary>
    public sealed class SetCommand : ICommand
    {
        private readonly IRoomBuilderSystem _roomBuilder;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "set";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Set a property on the current room.";
        public string LongDescription => "Sets the name or description of the room you are currently in.";
        public string Usage => "set <name|description> <value>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("property", typeof(string), CommandArgumentKind.Token,
                Required: true, "Property to set: name or description."),
            new CommandArgument("value", typeof(string), CommandArgumentKind.RestOfLine,
                Required: true, "New value."),
        });

        public SetCommand(IRoomBuilderSystem roomBuilder, EntityService entityService, IEventBus eventBus)
        {
            _roomBuilder = roomBuilder;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var property = context.Args.Get<string>("property");
            var value = context.Args.Get<string>("value");

            if (!string.Equals(property, "name", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property, "description", StringComparison.OrdinalIgnoreCase))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Unknown property '{property}'. Valid properties: name, description.",
                    OutputSeverity.Error)).ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var roomId = location.RoomEntityId;
            if (!_entityService.HasComponent<RoomComponent>(roomId))
            {
                await context.Output.WriteAsync(new PlainMessage("Your current location is not a room.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var normalizedProperty = property.ToLowerInvariant();
            if (normalizedProperty == "name")
                _roomBuilder.SetRoomName(roomId, value);
            else
                _roomBuilder.SetRoomDescription(roomId, value);

            await _eventBus.PublishAsync(new RoomPropertySetByAdminEvent(
                context.InvokerEntityId,
                roomId,
                normalizedProperty,
                value)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Room {normalizedProperty} set to '{value}'.",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
