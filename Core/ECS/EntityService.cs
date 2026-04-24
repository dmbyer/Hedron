using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Service that manages entities and components in the ECS architecture.
    /// </summary>
    /// <remarks>
    /// Entities are monotonically-allocated <c>uint</c> IDs, wrapped in <see cref="Entity"/> for
    /// call-site readability; state lives in components stored on a <see cref="ComponentRepository"/>.
    /// Systems and handlers consume this service rather than holding their own world state.
    /// Id <c>0</c> is reserved as a sentinel ("no entity") — allocation starts at <c>1</c>.
    /// </remarks>
    public class EntityService
    {
        private readonly ComponentRepository _componentRepository = new();
        private uint _nextId = 1;

        /// <summary>
        /// Allocates a new entity id. The returned <see cref="Entity"/> has no components yet —
        /// callers add the component composition appropriate to the archetype being built.
        /// </summary>
        public Entity CreateEntity()
        {
            return new Entity(_nextId++);
        }

        /// <summary>
        /// Adds (or replaces) a component on an entity.
        /// </summary>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            _componentRepository.AddComponent(entityId, component);
        }

        /// <summary>
        /// Gets a component from an entity. Throws <see cref="KeyNotFoundException"/> if the
        /// component is not present — use <see cref="TryGet{T}"/> when absence is expected.
        /// </summary>
        public T Get<T>(uint entityId) where T : class, IComponent
        {
            return _componentRepository.Get<T>(entityId);
        }

        /// <summary>
        /// Gets a component from an entity if present.
        /// </summary>
        public bool TryGet<T>(uint entityId, out T component) where T : class, IComponent
        {
            return _componentRepository.TryGet(entityId, out component);
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
