using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Account.Handlers;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Account
{
    /// <summary>
    /// Handler tests for <see cref="CharacterHydrationHandler"/> (on-touch ratchet): a character in an
    /// unresolvable room is reset to the resolved starting-room snapshot as a pair; a character in a
    /// live room is only re-pointed at that room's current entity.
    /// </summary>
    public sealed class CharacterHydrationHandlerTests
    {
        private sealed class RecordingPersistence : IPersistenceSystem
        {
            public List<uint> SavedEntityIds { get; } = new();

            public Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
            {
                SavedEntityIds.Add(entityId);
                return Task.CompletedTask;
            }

            public Task FlushAllAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task FlushDirtyAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<uint>>(Array.Empty<uint>());
        }

        private static (CharacterHydrationHandler handler, EntityService ecs, WorldConfiguration config, uint startRoom, uint otherRoom) Build()
        {
            var ecs = new EntityService();
            var startRoom = ecs.CreateEntity().Id;
            ecs.AddComponent(startRoom, new RoomComponent());
            ecs.AddComponent(startRoom, new BlueprintComponent { BlueprintId = "room.start" });
            var otherRoom = ecs.CreateEntity().Id;
            ecs.AddComponent(otherRoom, new RoomComponent());
            ecs.AddComponent(otherRoom, new BlueprintComponent { BlueprintId = "room.other" });

            var config = new WorldConfiguration
            {
                StartingRoom = new ResolvedStartingRoom(startRoom, "room.start", StartingRoomSource.Manifest, "room.start"),
            };
            var handler = new CharacterHydrationHandler(
                ecs, config, new RecordingPersistence(), NullLogger<CharacterHydrationHandler>.Instance);
            return (handler, ecs, config, startRoom, otherRoom);
        }

        private static uint MakeCharacter(EntityService ecs, string? roomBlueprintId, uint staleRoomEntityId = 999u)
        {
            var id = ecs.CreateEntity().Id;
            ecs.AddComponent(id, new CharacterComponent { CharacterName = "Tester" });
            ecs.AddComponent(id, new PersistentEntity());
            ecs.AddComponent(id, new LocationComponent { RoomEntityId = staleRoomEntityId, RoomBlueprintId = roomBlueprintId });
            return id;
        }

        [Fact]
        public async Task Character_in_unresolvable_room_is_reset_to_the_snapshot_pair()
        {
            var (handler, ecs, config, startRoom, _) = Build();
            var character = MakeCharacter(ecs, "room.deleted");

            await handler.HandleAsync(new WorldContentReadyEvent());

            var location = ecs.Get<LocationComponent>(character);
            Assert.Equal(startRoom, location.RoomEntityId);
            Assert.Equal(config.StartingRoom.BlueprintId, location.RoomBlueprintId);
        }

        [Fact]
        public async Task Character_in_live_room_is_repointed_not_reset()
        {
            var (handler, ecs, _, _, otherRoom) = Build();
            var character = MakeCharacter(ecs, "room.other");

            await handler.HandleAsync(new WorldContentReadyEvent());

            var location = ecs.Get<LocationComponent>(character);
            Assert.Equal(otherRoom, location.RoomEntityId);
            Assert.Equal("room.other", location.RoomBlueprintId);
        }
    }
}
