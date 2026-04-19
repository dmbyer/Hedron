using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Service that manages entities and components in the ECS architecture.
    /// </summary>
    /// <remarks>
    /// Entities are plain <c>uint</c> IDs; state lives in components stored on a
    /// <see cref="ComponentRepository"/>. Systems and handlers consume this service rather
    /// than holding their own world state.
    /// </remarks>
    public class EntityService
    {
        private readonly ComponentRepository _componentRepository = new ComponentRepository();

        /// <summary>
        /// Gets a component from an entity.
        /// </summary>
        public T GetComponent<T>(uint entityId) where T : class, IComponent
        {
            return _componentRepository.GetComponent<T>(entityId);
        }

        /// <summary>
        /// Adds a component to an entity.
        /// </summary>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            _componentRepository.AddComponent(entityId, component);
        }

        /// <summary>
        /// Checks if an entity has a specific component type.
        /// </summary>
        public bool HasComponent<T>(uint entityId) where T : IComponent
        {
            return _componentRepository.HasComponent<T>(entityId);
        }

        /// <summary>
        /// Checks if an entity has a specific component type.
        /// </summary>
        public bool HasComponent(uint entityId, Type componentType)
        {
            return _componentRepository.HasComponent(entityId, componentType);
        }

        /// <summary>
        /// Removes a component from an entity. Returns <c>true</c> if a component was removed.
        /// </summary>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            return _componentRepository.RemoveComponent<T>(entityId);
        }

        /// <summary>
        /// Gets all entity IDs that have a specific component type.
        /// </summary>
        public IEnumerable<uint> GetEntitiesWith<T>() where T : IComponent
        {
            return _componentRepository.GetEntitiesWith<T>();
        }

        /// <summary>
        /// Gets all components of a specific type paired with their entity IDs.
        /// </summary>
        public IEnumerable<(uint EntityId, T Component)> GetAllComponents<T>() where T : class, IComponent
        {
            return _componentRepository.GetAllComponents<T>();
        }

        /// <summary>
        /// Removes all components for a given entity.
        /// </summary>
        public void DestroyEntity(uint entityId)
        {
            _componentRepository.RemoveAllComponents(entityId);
        }
    }
}
