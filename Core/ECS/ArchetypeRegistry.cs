using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using System;
using System.Collections.Generic;

namespace Core.ECS
{
	/// <summary>
	/// Central registry of entity archetype definitions
	/// </summary>
	public static class ArchetypeRegistry
	{
		private static readonly Dictionary<EntityArchetype, ArchetypeDefinition> _definitions = 
			new Dictionary<EntityArchetype, ArchetypeDefinition>();

		static ArchetypeRegistry()
		{
			InitializeArchetypes();
		}

		/// <summary>
		/// Gets the archetype definition for the specified archetype
		/// </summary>
		public static ArchetypeDefinition GetDefinition(EntityArchetype archetype)
		{
			return _definitions.TryGetValue(archetype, out var definition) ? definition : null;
		}

		/// <summary>
		/// Gets all registered archetype definitions
		/// </summary>
		public static IEnumerable<ArchetypeDefinition> GetAllDefinitions()
		{
			return _definitions.Values;
		}

		/// <summary>
		/// Detects the archetype of an entity based on its components
		/// </summary>
		public static EntityArchetype DetectArchetype(uint entityId, EntityService entityService)
		{
			foreach (var kvp in _definitions)
			{
				if (kvp.Value.ValidateEntity(entityId, entityService))
					return kvp.Key;
			}
			return EntityArchetype.Custom;
		}

		private static void InitializeArchetypes()
		{
			// Living Entities
			_definitions[EntityArchetype.Player] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Player,
				Description = "Player character with full RPG stats, inventory, and equipment",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(AttributesComponent),
					typeof(PoolsComponent),
					typeof(CurrencyComponent),
					typeof(SkillsComponent),
					typeof(QualitiesComponent),
					typeof(InventoryComponent),
					typeof(EquipmentComponent),
					typeof(PlayerDataComponent),
					typeof(PlayerConfigurationComponent)
				}
			};

			_definitions[EntityArchetype.Mob] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Mob,
				Description = "NPC/monster with AI and combat capabilities, inventory, and equipment",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(AttributesComponent),
					typeof(PoolsComponent),
					typeof(CurrencyComponent),
					typeof(SkillsComponent),
					typeof(QualitiesComponent),
					typeof(InventoryComponent),
					typeof(EquipmentComponent),
					typeof(MobDataComponent)
				}
			};

			// Items
			_definitions[EntityArchetype.Weapon] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Weapon,
				Description = "Equippable weapon with combat stats",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ItemDataComponent),
					typeof(WeaponDataComponent)
				}
			};

			_definitions[EntityArchetype.Armor] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Armor,
				Description = "Equippable armor with defensive stats",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ItemDataComponent)
					// Note: ArmorDataComponent would be created later for armor-specific stats
				},
				OptionalComponents = new HashSet<Type>
				{
					typeof(EquipmentDataComponent) // Placeholder for future armor component
				}
			};

			_definitions[EntityArchetype.Potion] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Potion,
				Description = "Consumable potion with restoration or effect properties",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ItemDataComponent),
					typeof(PotionDataComponent)
				}
			};

			_definitions[EntityArchetype.StaticItem] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.StaticItem,
				Description = "Non-equippable item like furniture or decoration",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ItemDataComponent)
				}
			};

			_definitions[EntityArchetype.Consumable] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Consumable,
				Description = "Single-use consumable item",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ItemDataComponent)
				}
			};

			// Containers
			_definitions[EntityArchetype.Room] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Room,
				Description = "Game world room with exits and container functionality",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ContainerDataComponent),
					typeof(RoomDataComponent),
					typeof(InventoryComponent)
				}
			};

			_definitions[EntityArchetype.Area] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Area,
				Description = "Game world area containing multiple rooms",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ContainerDataComponent),
					typeof(AreaDataComponent)
				}
			};

			_definitions[EntityArchetype.World] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.World,
				Description = "Top-level world container holding all areas",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ContainerDataComponent)
				}
			};

			_definitions[EntityArchetype.Storage] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Storage,
				Description = "Storage container like chest or corpse",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent),
					typeof(ContainerDataComponent)
				}
			};

			_definitions[EntityArchetype.Inventory] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Inventory,
				Description = "Player or mob inventory container",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(ContainerDataComponent)
				}
			};

			// Special
			_definitions[EntityArchetype.Portal] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Portal,
				Description = "Transportation portal between areas or worlds",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent),
					typeof(EffectsComponent)
					// Note: PortalDataComponent would be created for portal-specific functionality
				}
			};

			_definitions[EntityArchetype.Trigger] = new ArchetypeDefinition
			{
				Archetype = EntityArchetype.Trigger,
				Description = "Invisible trigger zone for scripted events",
				RequiredComponents = new HashSet<Type>
				{
					typeof(IdentityComponent),
					typeof(TransformComponent),
					typeof(PrototypeComponent)
					// Note: TriggerDataComponent would be created for trigger-specific functionality
				}
			};
		}
	}
}