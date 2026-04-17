using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Service that manages entities, components, and systems in the ECS architecture
    /// </summary>
    public class EntityService
    {
        private readonly ComponentRepository _componentRepository = new ComponentRepository();
        private readonly Dictionary<Type, IModule> _modules = new Dictionary<Type, IModule>();

        /// <summary>
        /// Gets a component from an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>The component instance or null if not found</returns>
        public T GetComponent<T>(uint entityId) where T : class, IComponent
        {
            return _componentRepository.GetComponent<T>(entityId);
        }

        /// <summary>
        /// Adds a component to an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <param name="component">The component instance</param>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            _componentRepository.AddComponent(entityId, component);
        }

        /// <summary>
        /// Checks if an entity has a specific component type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the entity has the component</returns>
        public bool HasComponent<T>(uint entityId) where T : IComponent
        {
            return _componentRepository.HasComponent<T>(entityId);
        }

        /// <summary>
        /// Checks if an entity has a specific component type
        /// </summary>
        /// <param name="entityId">The entity ID</param>
        /// <param name="componentType">The component type</param>
        /// <returns>True if the entity has the component</returns>
        public bool HasComponent(uint entityId, Type componentType)
        {
            return _componentRepository.HasComponent(entityId, componentType);
        }

        /// <summary>
        /// Removes a component from an entity
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the component was removed</returns>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            return _componentRepository.RemoveComponent<T>(entityId);
        }

        /// <summary>
        /// Gets all entities that have a specific component type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<uint> GetEntitiesWith<T>() where T : IComponent
        {
            return _componentRepository.GetEntitiesWith<T>();
        }

        /// <summary>
        /// Gets all components of a specific type
        /// </summary>
        /// <typeparam name="T">The component type</typeparam>
        /// <returns>Collection of component instances with their entity IDs</returns>
        public IEnumerable<(uint EntityId, T Component)> GetAllComponents<T>() where T : class, IComponent
        {
            return _componentRepository.GetAllComponents<T>();
        }

        /// <summary>
        /// Registers a module with the service
        /// </summary>
        /// <typeparam name="T">The module type</typeparam>
        /// <param name="module">The module instance</param>
        public void RegisterModule<T>(T module) where T : IModule
        {
            _modules[typeof(T)] = module;
        }

        /// <summary>
        /// Gets a registered module
        /// </summary>
        /// <typeparam name="T">The module type</typeparam>
        /// <returns>The module instance or null if not registered</returns>
        public T GetModule<T>() where T : class, IModule
        {
            if (_modules.TryGetValue(typeof(T), out var module))
            {
                return module as T;
            }
            return null;
        }

        /// <summary>
        /// Removes all components for a given entity
        /// </summary>
        /// <param name="entityId">The entity ID</param>
        public void DestroyEntity(uint entityId)
        {
            _componentRepository.RemoveAllComponents(entityId);
        }
    }
}