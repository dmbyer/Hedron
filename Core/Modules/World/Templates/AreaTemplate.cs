using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Templates
{
    /// <summary>
    /// Authored area blueprint. Areas are entities with an <see cref="AreaComponent"/> describing
    /// metadata that future slices will consume (mob respawn rate, pvp toggle, theming hooks).
    /// </summary>
    public sealed class AreaTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string AreaId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int RespawnRate { get; set; }
        public bool Pvp { get; set; }
        public List<string> Rooms { get; } = new();

        public AreaTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new AreaComponent
            {
                AreaId = string.IsNullOrEmpty(AreaId) ? BlueprintId : AreaId,
                Name = Name,
                Description = Description,
                RespawnRate = RespawnRate,
                Pvp = Pvp,
            });
        }
    }
}
