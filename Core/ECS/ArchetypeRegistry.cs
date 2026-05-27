using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Default <see cref="IArchetypeRegistry"/>. Defines the required/optional component
    /// composition for each <see cref="EntityArchetype"/> and implements validation and
    /// detection logic.
    ///
    /// <para><b>Current-state note:</b> Only archetypes whose required components are
    /// fully implemented appear in the definitions. Planned-but-not-yet-built components
    /// (Identity, Transform, Currency, Skills, etc.) are tracked in
    /// <c>docs/reference/archetypes.md</c> and <c>docs/roadmap/backlog.md</c>; they will
    /// be added to the relevant definition as the components land.</para>
    ///
    /// <para><b>Detection order</b> runs from most-specific to least-specific to avoid
    /// false positives when required sets overlap.</para>
    /// </summary>
    public sealed class ArchetypeRegistry : IArchetypeRegistry
    {
        private readonly EntityService _entityService;

        // Ordered list of (archetype, detection-predicate) pairs.
        // Checked top-to-bottom; first match wins.
        private readonly (EntityArchetype Archetype, Func<uint, bool> Matches)[] _detectionOrder;

        private readonly Dictionary<EntityArchetype, ArchetypeDefinition> _definitions;

        public ArchetypeRegistry(EntityService entityService)
        {
            _entityService = entityService;
            _definitions = BuildDefinitions();
            _detectionOrder = BuildDetectionOrder();
        }

        // ── IArchetypeRegistry ────────────────────────────────────────────────

        public IReadOnlyList<Type> RequiredComponents(EntityArchetype archetype)
            => _definitions.TryGetValue(archetype, out var def) ? def.Required : Array.Empty<Type>();

        public IReadOnlyList<Type> OptionalComponents(EntityArchetype archetype)
            => _definitions.TryGetValue(archetype, out var def) ? def.Optional : Array.Empty<Type>();

        public bool Validate(uint entityId, EntityArchetype expected)
            => !MissingRequired(entityId, expected).Any();

        public EntityArchetype Detect(uint entityId)
        {
            foreach (var (archetype, matches) in _detectionOrder)
            {
                if (matches(entityId))
                    return archetype;
            }
            return EntityArchetype.Custom;
        }

        public IEnumerable<Type> MissingRequired(uint entityId, EntityArchetype archetype)
        {
            if (!_definitions.TryGetValue(archetype, out var def))
                yield break;

            foreach (var type in def.Required)
            {
                if (!_entityService.HasComponent(entityId, type))
                    yield return type;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static Dictionary<EntityArchetype, ArchetypeDefinition> BuildDefinitions()
        {
            // Only components that are actually implemented today are listed here.
            // Planned-but-absent components are noted in docs/reference/archetypes.md.
            return new Dictionary<EntityArchetype, ArchetypeDefinition>
            {
                [EntityArchetype.Mob] = new ArchetypeDefinition
                {
                    Archetype = EntityArchetype.Mob,
                    Required = new Type[]
                    {
                        typeof(MobDataComponent),
                        typeof(AttributesComponent),
                        typeof(PoolsComponent),
                    },
                    Optional = new Type[]
                    {
                        // InventoryComponent and EquipmentComponent are planned for mobs
                        // but not yet added to mob construction (tracked in backlog).
                        typeof(InventoryComponent),
                        typeof(EquipmentComponent),
                    },
                },

                [EntityArchetype.Player] = new ArchetypeDefinition
                {
                    Archetype = EntityArchetype.Player,
                    Required = new Type[]
                    {
                        // CharacterComponent is the persistent identity marker for player entities.
                        // PlayerComponent is transient (session-only) and is excluded here because
                        // it is not present during startup migration before sessions attach.
                        typeof(CharacterComponent),
                        typeof(AttributesComponent),
                        typeof(PoolsComponent),
                        typeof(InventoryComponent),
                        typeof(EquipmentComponent),
                    },
                    Optional = Array.Empty<Type>(),
                },

                [EntityArchetype.Room] = new ArchetypeDefinition
                {
                    Archetype = EntityArchetype.Room,
                    Required = new Type[] { typeof(RoomComponent) },
                    Optional = Array.Empty<Type>(),
                },

                [EntityArchetype.Area] = new ArchetypeDefinition
                {
                    Archetype = EntityArchetype.Area,
                    Required = new Type[] { typeof(AreaComponent) },
                    Optional = Array.Empty<Type>(),
                },

                // All item sub-archetypes (Weapon, Armor, Potion, etc.) share ItemDataComponent
                // as their minimum required component. Sub-archetype differentiation via
                // additional data components (WeaponDataComponent, etc.) will be added as those
                // components land. Until then, StaticItem serves as the catch-all item archetype.
                [EntityArchetype.StaticItem] = new ArchetypeDefinition
                {
                    Archetype = EntityArchetype.StaticItem,
                    Required = new Type[] { typeof(ItemDataComponent) },
                    Optional = Array.Empty<Type>(),
                },
            };
        }

        private (EntityArchetype, Func<uint, bool>)[] BuildDetectionOrder()
        {
            // Order matters: more specific archetypes must be checked before less specific ones
            // so that an entity matching multiple required sets is classified correctly.
            return new (EntityArchetype, Func<uint, bool>)[]
            {
                (EntityArchetype.Mob,        id => _entityService.HasComponent<MobDataComponent>(id)),
                (EntityArchetype.Player,     id => _entityService.HasComponent<CharacterComponent>(id)),
                (EntityArchetype.Room,       id => _entityService.HasComponent<RoomComponent>(id)),
                (EntityArchetype.Area,       id => _entityService.HasComponent<AreaComponent>(id)),
                // Item check is last among defined archetypes; Weapon/Armor/etc. will be
                // inserted above this line as their marker components are implemented.
                (EntityArchetype.StaticItem, id => _entityService.HasComponent<ItemDataComponent>(id)),
                // EntityArchetype.Custom is the implicit fallback returned by Detect().
            };
        }
    }
}
