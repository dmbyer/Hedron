using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Server
{
    /// <summary>
    /// Constructs the hand-authored three-room world at host startup.
    /// No data files, no editor — rooms are declared directly in code for MVP.
    /// </summary>
    internal static class WorldBootstrap
    {
        public static void Initialize(EntityService entityService, WorldConfiguration worldConfig)
        {
            // --- Create rooms ---
            var westEnd   = entityService.CreateEntity();
            var crossroads = entityService.CreateEntity();
            var eastEnd   = entityService.CreateEntity();

            entityService.AddComponent(westEnd.Id, new RoomComponent
            {
                Name = "West End",
                Description = "A dusty road stretches eastward toward the crossroads.",
            });

            entityService.AddComponent(crossroads.Id, new RoomComponent
            {
                Name = "The Crossroads",
                Description = "A broad stone crossroads. Roads branch east and west; the air smells faintly of wood smoke.",
            });

            entityService.AddComponent(eastEnd.Id, new RoomComponent
            {
                Name = "East End",
                Description = "The road ends here atop a low hill, overlooking a vast plain.",
            });

            // --- Wire exits ---
            entityService.Get<RoomComponent>(westEnd.Id).Exits[Direction.East]  = crossroads.Id;

            entityService.Get<RoomComponent>(crossroads.Id).Exits[Direction.West] = westEnd.Id;
            entityService.Get<RoomComponent>(crossroads.Id).Exits[Direction.East] = eastEnd.Id;

            entityService.Get<RoomComponent>(eastEnd.Id).Exits[Direction.West]  = crossroads.Id;

            // --- Set starting room ---
            worldConfig.StartingRoomEntityId = crossroads.Id;
        }
    }
}
