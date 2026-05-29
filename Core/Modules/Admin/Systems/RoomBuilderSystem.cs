using System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Implements runtime room authoring: creation, exit wiring, and property mutation.
    /// Commands are thin orchestrators; this system holds the domain logic so a future
    /// in-game editor can reuse the same operations without a live player session.
    /// </summary>
    public sealed class RoomBuilderSystem : IRoomBuilderSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly ILogger<RoomBuilderSystem> _logger;

        public RoomBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            ILogger<RoomBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _logger = logger;
        }

        public RoomCreationResult CreateRoom(string name, string description = "")
        {
            var blueprintId = GenerateUniqueBlueprintId();

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new RoomComponent { Name = name, Description = description });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });

            var template = new RoomTemplate(blueprintId) { Name = name, Description = description };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug("RoomBuilderSystem: created room entity={EntityId} blueprint={BlueprintId}", entity.Id, blueprintId);
            return new RoomCreationResult(entity.Id, blueprintId);
        }

        public void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional)
        {
            if (_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
                sourceRoom.Exits[direction] = targetRoomId;

            MirrorExitToTemplate(sourceRoomId, direction, targetRoomId);

            if (bidirectional)
            {
                var opposite = Opposite(direction);
                if (opposite is not null && _entityService.TryGet<RoomComponent>(targetRoomId, out var targetRoom))
                    targetRoom.Exits[opposite.Value] = sourceRoomId;

                if (opposite is not null)
                    MirrorExitToTemplate(targetRoomId, opposite.Value, sourceRoomId);
            }
        }

        public void SetRoomName(uint roomId, string name)
        {
            if (_entityService.TryGet<RoomComponent>(roomId, out var room))
                room.Name = name;
            var tpl = TryGetTemplate(roomId);
            if (tpl is not null) tpl.Name = name;
        }

        public void SetRoomDescription(uint roomId, string description)
        {
            if (_entityService.TryGet<RoomComponent>(roomId, out var room))
                room.Description = description;
            var tpl = TryGetTemplate(roomId);
            if (tpl is not null) tpl.Description = description;
        }

        private string GenerateUniqueBlueprintId()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = "room.adhoc." + ToBase36(Guid.NewGuid())[..8];
                if (!_templateRegistry.TryGet(id, out _))
                    return id;
            }
            // Fallback: use full guid suffix — collision-free by construction
            return "room.adhoc." + Guid.NewGuid().ToString("N")[..16];
        }

        private static string ToBase36(Guid guid)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var bytes = guid.ToByteArray();
            var value = Math.Abs(BitConverter.ToInt64(bytes, 0));
            if (value == 0) return "0";
            var result = new System.Text.StringBuilder();
            while (value > 0)
            {
                result.Insert(0, chars[(int)(value % 36)]);
                value /= 36;
            }
            return result.ToString();
        }

        private void MirrorExitToTemplate(uint roomId, Direction direction, uint targetRoomId)
        {
            if (!_entityService.TryGet<BlueprintComponent>(roomId, out var sourceBp)) return;
            if (!_entityService.TryGet<BlueprintComponent>(targetRoomId, out var targetBp)) return;
            if (!_templateRegistry.TryGet(sourceBp.BlueprintId, out var template)) return;
            if (template is RoomTemplate roomTemplate)
                roomTemplate.Exits[direction] = targetBp.BlueprintId;
        }

        private RoomTemplate? TryGetTemplate(uint roomId)
        {
            if (_entityService.TryGet<BlueprintComponent>(roomId, out var bp) &&
                _templateRegistry.TryGet(bp.BlueprintId, out var template) &&
                template is RoomTemplate roomTemplate)
                return roomTemplate;
            return null;
        }

        private static Direction? Opposite(Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _               => null,
        };
    }
}
