using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Repository for component storage and retrieval in the ECS system
    /// </summary>
    public class ComponentRepository
    {
        private readonly Dictionary<Type, Dictionary<uint, IComponent>> _components = new Dictionary<Type, Dictionary<uint, IComponent>>();

        /// <summary>
        /// Adds a component to an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <param name="component">The component instance</param>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            var componentType = typeof(T);
            
            if (!_components.ContainsKey(componentType))
                _components[componentType] = new Dictionary<uint, IComponent>();
                
            _components[componentType][entityId] = component;
        }

        /// <summary>
        /// Gets a component from an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>The component instance or null if not found</returns>
        public T GetComponent<T>(uint entityId) where T : class, IComponent
        {
            var componentType = typeof(T);
            
            if (_components.TryGetValue(componentType, out var componentDict) &&
                componentDict.TryGetValue(entityId, out var component))
            {
                return component as T;
            }
            
            return null;
        }

        /// <summary>
        /// Checks if an entity has a specific component type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the entity has the component</returns>
        public bool HasComponent<T>(uint entityId) where T : IComponent
        {
            var componentType = typeof(T);
            return _components.TryGetValue(componentType, out var componentDict) && 
                   componentDict.ContainsKey(entityId);
        }

        /// <summary>
        /// Checks if an entity has a specific component type
        /// </summary>
        /// <param name="entityId">The entity ID</param>
        /// <param name="componentType">The component type</param>
        /// <returns>True if the entity has the component</returns>
        public bool HasComponent(uint entityId, Type componentType)
        {
            return _components.TryGetValue(componentType, out var componentDict) && 
                   componentDict.ContainsKey(entityId);
        }

        /// <summary>
        /// Removes a component from an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the component was removed</returns>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            var componentType = typeof(T);
            
            if (_components.TryGetValue(componentType, out var componentDict))
            {
                return componentDict.Remove(entityId);
            }
            
            return false;
        }

        /// <summary>
        /// Gets all entities that have a specific component type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<uint> GetEntitiesWith<T>() where T : IComponent
        {
            var componentType = typeof(T);
            
            if (_components.TryGetValue(componentType, out var componentDict))
            {
                return componentDict.Keys;
            }
            
            return Enumerable.Empty<uint>();
        }

        /// <summary>
        /// Gets all components of a specific type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <returns>Collection of component instances with their entity IDs</returns>
        public IEnumerable<(uint EntityId, T Component)> GetAllComponents<T>() where T : class, IComponent
        {
            var componentType = typeof(T);
            
            if (_components.TryGetValue(componentType, out var componentDict))
            {
                return componentDict.Select(kvp => (kvp.Key, kvp.Value as T)).Where(x => x.Item2 != null);
            }
            
            return Enumerable.Empty<(uint, T)>();
        }

        /// <summary>
        /// Removes all components for a given entity
        /// </summary>
        /// <param name="entityId">The entity ID</param>
        public void RemoveAllComponents(uint entityId)
        {
            foreach (var componentDict in _components.Values)
            {
                componentDict.Remove(entityId);
            }
        }
    }
}