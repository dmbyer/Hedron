using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Templates
{
    public sealed class MobTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;

        /// <summary>Blueprint id of the room this mob spawns in. Empty means no spawn location.</summary>
        public string SpawnRoomBlueprintId { get; set; } = string.Empty;

        public MobTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new MobDataComponent
            {
                Name = Name,
                Description = Description,
                Keywords = new List<string>(Keywords),
                MobType = MobType,
            });
        }
    }
}
