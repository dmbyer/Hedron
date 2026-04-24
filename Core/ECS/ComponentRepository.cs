using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Repository for component storage and retrieval in the ECS system.
    /// </summary>
    public class ComponentRepository
    {
        private readonly Dictionary<Type, Dictionary<uint, IComponent>> _components = new();

        /// <summary>
        /// Adds (or replaces) a component on an entity.
        /// </summary>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            var componentType = typeof(T);

            if (!_components.TryGetValue(componentType, out var componentDict))
            {
                componentDict = new Dictionary<uint, IComponent>();
                _components[componentType] = componentDict;
            }

            componentDict[entityId] = component!;
        }

        /// <summary>
        /// Gets a component from an entity. Throws <see cref="KeyNotFoundException"/> if missing —
        /// use <see cref="TryGet{T}"/> when absence is expected.
        /// </summary>
        public T Get<T>(uint entityId) where T : class, IComponent
        {
            if (TryGet<T>(entityId, out var component))
                return component;

            throw new KeyNotFoundException(
                $"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        /// <summary>
        /// Gets a component from an entity if present.
        /// </summary>
        public bool TryGet<T>(uint entityId, out T component) where T : class, IComponent
        {
            if (_components.TryGetValue(typeof(T), out var componentDict) &&
                componentDict.TryGetValue(entityId, out var stored))
            {
                component = (T)stored;
                return true;
            }

            component = null!;
            return false;
        }

        /// <summary>
        /// Checks if an entity has a specific component type.
        /// </summary>
        public bool HasComponent<T>(uint entityId) where T : IComponent
        {
            return _components.TryGetValue(typeof(T), out var componentDict) &&
                   componentDict.ContainsKey(entityId);
        }

        /// <summary>
        /// Checks if an entity has a specific component type.
        /// </summary>
        public bool HasComponent(uint entityId, Type componentType)
        {
            return _components.TryGetValue(componentType, out var componentDict) &&
                   componentDict.ContainsKey(entityId);
        }

        /// <summary>
        /// Removes a component from an entity. Returns <c>true</c> if a component was removed.
        /// </summary>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            if (_components.TryGetValue(typeof(T), out var componentDict))
                return componentDict.Remove(entityId);

            return false;
        }

        /// <summary>
        /// Gets all entity IDs that have a specific component type.
        /// </summary>
        public IEnumerable<uint> GetEntitiesWith<T>() where T : IComponent
        {
            if (_components.TryGetValue(typeof(T), out var componentDict))
                return componentDict.Keys;

            return Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Gets all components of a specific type paired with their entity IDs.
        /// </summary>
        public IEnumerable<(uint EntityId, T Component)> GetAllComponents<T>() where T : class, IComponent
        {
            if (_components.TryGetValue(typeof(T), out var componentDict))
                return componentDict.Select(kvp => (kvp.Key, (T)kvp.Value));

            return Enumerable.Empty<(uint, T)>();
        }

        /// <summary>
        /// Removes all components for a given entity.
        /// </summary>
        public void RemoveAllComponents(uint entityId)
        {
            foreach (var componentDict in _components.Values)
                componentDict.Remove(entityId);
        }
    }
}
