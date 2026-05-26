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

        public int Level { get; set; } = 0;
        public int MaxHp { get; set; } = 0;
        public int Strength { get; set; } = 0;
        public int Dexterity { get; set; } = 0;
        public int Constitution { get; set; } = 0;

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

            var level = Level > 0 ? Level : 1;
            var maxHp = MaxHp > 0 ? MaxHp : 100;
            entityService.AddComponent(entity.Id, new AttributesComponent
            {
                Level = level,
                Strength = Strength > 0 ? Strength : 10,
                Dexterity = Dexterity > 0 ? Dexterity : 10,
                Constitution = Constitution > 0 ? Constitution : 10,
            });
            entityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = maxHp,
                CurrentHp = maxHp,
            });
        }
    }
}
