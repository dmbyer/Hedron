using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using System;
using System.Collections.Generic;

namespace Core.ECS
{
	/// <summary>
	/// Defines the component composition for an entity archetype
	/// </summary>
	public class ArchetypeDefinition
	{
		/// <summary>
		/// The archetype this definition represents
		/// </summary>
		public EntityArchetype Archetype { get; set; }

		/// <summary>
		/// Component types that must be present on entities of this archetype
		/// </summary>
		public HashSet<Type> RequiredComponents { get; set; } = new HashSet<Type>();

		/// <summary>
		/// Component types that are optional for entities of this archetype
		/// </summary>
		public HashSet<Type> OptionalComponents { get; set; } = new HashSet<Type>();

		/// <summary>
		/// Human-readable description of this archetype
		/// </summary>
		public string Description { get; set; } = "";

		/// <summary>
		/// Validates that an entity matches this archetype definition
		/// </summary>
		/// <param name="entityId">Entity ID to validate</param>
		/// <param name="entityService">Entity service containing the entity</param>
		/// <returns>True if entity matches archetype, false otherwise</returns>
		public bool ValidateEntity(uint entityId, EntityService entityService)
		{
			// Check that all required components are present
			foreach (var componentType in RequiredComponents)
			{
				if (!entityService.HasComponent(entityId, componentType))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Gets all component types (required + optional) for this archetype
		/// </summary>
		public HashSet<Type> GetAllComponents()
		{
			var all = new HashSet<Type>(RequiredComponents);
			foreach (var optional in OptionalComponents)
				all.Add(optional);
			return all;
		}
	}
}