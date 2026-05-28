using System;
using System.Collections.Generic;
using Hedron.Core.ECS.Components;

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

        // Tracks which entities carry PersistentEntity so DestroyEntity can clean up SQLite rows
        // even after the component has been removed during teardown. Populated via AddComponent hooks;
        // depleted via RemoveComponent<PersistentEntity> and DestroyEntity.
        private readonly HashSet<uint> _persistentEntityIds = new();

        /// <summary>
        /// Invoked synchronously inside <see cref="DestroyEntity"/> before ECS teardown, for every
        /// entity that carried <c>PersistentEntity</c>. Registered by <c>PersistenceSystem</c> to
        /// issue the SQLite DELETE. Must not throw — exceptions are silently swallowed by the caller.
        /// </summary>
        public Action<uint>? OnPersistentEntityDestroying;

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
        /// When the component is <c>PersistentEntity</c>, the entity id is recorded in the
        /// internal persistence set so <see cref="DestroyEntity"/> can trigger the auto-delete.
        /// </summary>
        public void AddComponent<T>(uint entityId, T component) where T : IComponent
        {
            _componentRepository.AddComponent(entityId, component);
            if (typeof(T) == typeof(PersistentEntity))
                _persistentEntityIds.Add(entityId);
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
        /// When the component is <c>PersistentEntity</c>, the entity id is removed from the
        /// internal persistence set so a subsequent <see cref="DestroyEntity"/> will not trigger
        /// the auto-delete for a no-longer-persistent entity.
        /// </summary>
        public bool RemoveComponent<T>(uint entityId) where T : IComponent
        {
            var removed = _componentRepository.RemoveComponent<T>(entityId);
            if (removed && typeof(T) == typeof(PersistentEntity))
                _persistentEntityIds.Remove(entityId);
            return removed;
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
        /// Adds (or replaces) a component on an entity using a runtime <see cref="Type"/>.
        /// Used by <c>PersistenceSystem</c> during hydration when the concrete type is only
        /// known at runtime.
        /// When the component type is <c>PersistentEntity</c>, the entity id is recorded in the
        /// internal persistence set.
        /// </summary>
        public void AddComponent(uint entityId, Type componentType, IComponent component)
        {
            _componentRepository.AddComponent(entityId, componentType, component);
            if (componentType == typeof(PersistentEntity))
                _persistentEntityIds.Add(entityId);
        }

        /// <summary>
        /// Returns all components attached to the given entity as (Type, IComponent) pairs.
        /// Used by <c>PersistenceSystem</c> to enumerate an entity's full component set before
        /// serialization.
        /// </summary>
        public IEnumerable<(Type ComponentType, IComponent Component)> GetAllComponentsForEntity(uint entityId)
        {
            return _componentRepository.GetAllForEntity(entityId);
        }

        /// <summary>
        /// Restores an entity with a specific <paramref name="id"/> (e.g. loaded from disk).
        /// Advances the internal counter past <paramref name="id"/> so that subsequent
        /// <see cref="CreateEntity"/> calls never collide with a restored entity.
        /// </summary>
        /// <remarks>
        /// Call this during hydration (<c>PersistenceSystem.LoadAllAsync</c>) instead of
        /// <see cref="CreateEntity"/> so that persisted entity IDs are preserved.
        /// </remarks>
        public Entity RestoreEntity(uint id)
        {
            if (id >= _nextId)
                _nextId = id + 1;
            return new Entity(id);
        }

        /// <summary>
        /// Removes all components for a given entity. If the entity carried <c>PersistentEntity</c>,
        /// fires <see cref="OnPersistentEntityDestroying"/> before teardown so the persistence
        /// backend can delete the entity's SQLite rows.
        /// </summary>
        public void DestroyEntity(uint entityId)
        {
            if (_persistentEntityIds.Remove(entityId))
            {
                try { OnPersistentEntityDestroying?.Invoke(entityId); }
                catch { /* Persistence errors must not abort ECS teardown. */ }
            }

            _componentRepository.RemoveAllComponents(entityId);
        }
    }
}
