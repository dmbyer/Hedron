using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Templates
{
    /// <summary>
    /// Authored room blueprint. Carries human-readable name/description, exit map keyed by
    /// <see cref="Direction"/> → target room blueprint id, and the area this room belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exits are stored by <i>blueprint id</i> in the template. The runtime exit map on
    /// <see cref="RoomComponent"/> is keyed by entity id; <c>WorldContentLoader</c> resolves
    /// blueprint ids to entity ids in a second-pass linking phase, after every room template
    /// has been spawned.
    /// </para>
    /// <para>
    /// <see cref="Apply"/> attaches a <see cref="RoomComponent"/> with name and description set
    /// but the runtime exit map left empty — the loader populates it during the linking pass.
    /// If <see cref="SpawnRules"/> is non-empty, also attaches a <see cref="SpawnConfigComponent"/>.
    /// </para>
    /// </remarks>
    public sealed class RoomTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AreaId { get; set; } = string.Empty;

        /// <summary>
        /// Declared schema version, as authored in YAML. <c>null</c> when the file omits it.
        /// Round-tripped as-is (no rewriting to <c>CurrentSchemaVersion</c>) — the deserializer's
        /// mismatch warning is a read-time concern, not a write-time normalization.
        /// </summary>
        public int? SchemaVersion { get; set; }

        /// <summary>
        /// Optional authoring-side grid coordinates (visual grid editor). East = X+1, North = Y+1,
        /// Up = Z+1. Advisory only — <see cref="Apply"/> attaches no coordinate-bearing runtime
        /// component this slice.
        /// </summary>
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Z { get; set; }

        /// <summary>Exits keyed by direction; values are target room blueprint ids.</summary>
        public Dictionary<Direction, string> Exits { get; } = new();

        /// <summary>Spawn rules declared in YAML; each entry is one respawnable slot.</summary>
        public List<SpawnRule> SpawnRules { get; } = new();

        public RoomTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new RoomComponent
            {
                Name = Name,
                Description = Description,
            });

            if (SpawnRules.Count > 0)
            {
                var spawnConfig = new SpawnConfigComponent();
                spawnConfig.Rules.AddRange(SpawnRules);
                entityService.AddComponent(entity.Id, spawnConfig);
            }
        }
    }
}
