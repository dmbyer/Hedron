using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
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

        /// <summary>
        /// Declared schema version, as authored in YAML. <c>null</c> when the file omits it.
        /// Round-tripped as-is (no rewriting to the deserializer's current version).
        /// </summary>
        public int? SchemaVersion { get; set; }

        public string AreaId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int RespawnRate { get; set; }
        public bool Pvp { get; set; }
        public List<string> Rooms { get; } = new();

        /// <summary>
        /// Optional elemental affinities for this area. When set (non-empty), attaches an
        /// <see cref="AspectAffinitiesComponent"/> so gameplay systems can weight ambient damage,
        /// mob spawn selection, and future resist/bonus calculations by area theme.
        /// </summary>
        public Dictionary<AspectId, int>? AspectAffinities { get; set; }

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

            if (AspectAffinities is { Count: > 0 })
            {
                entityService.AddComponent(entity.Id, new AspectAffinitiesComponent
                {
                    AffinityWeights = new Dictionary<AspectId, int>(AspectAffinities),
                });
            }
        }
    }
}
