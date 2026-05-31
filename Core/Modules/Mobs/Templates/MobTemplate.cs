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
        public int Mind { get; set; } = 0;
        public int Body { get; set; } = 0;
        public int Spirit { get; set; } = 0;
        public int Attunement { get; set; } = 0;
        public int MaxMana { get; set; } = 0;
        public int MaxStamina { get; set; } = 0;
        public int MaxAstra { get; set; } = 0;

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
                Mind = Mind > 0 ? Mind : 10,
                Body = Body > 0 ? Body : 10,
                Spirit = Spirit > 0 ? Spirit : 10,
                Attunement = Attunement > 0 ? Attunement : 10,
            });
            var maxMana = MaxMana > 0 ? MaxMana : 50;
            var maxStamina = MaxStamina > 0 ? MaxStamina : 50;
            var maxAstra = MaxAstra > 0 ? MaxAstra : 10;
            entityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = maxHp,
                CurrentHp = maxHp,
                MaxMana = maxMana,
                CurrentMana = maxMana,
                MaxStamina = maxStamina,
                CurrentStamina = maxStamina,
                MaxAstra = maxAstra,
                CurrentAstra = maxAstra,
            });
        }
    }
}
