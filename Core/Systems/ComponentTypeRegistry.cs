using System.Reflection;
using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Reflection-built registry of all <see cref="IComponent"/> implementations.
    /// Populated once at construction by scanning the assembly that defines <see cref="IComponent"/>.
    /// </summary>
    public sealed class ComponentTypeRegistry : IComponentTypeRegistry
    {
        private readonly Dictionary<Type, bool> _persistenceMap;
        private readonly Dictionary<string, Type> _nameMap;
        private readonly IReadOnlyList<Type> _persistentTypes;

        public ComponentTypeRegistry()
        {
            var componentInterface = typeof(IComponent);
            var assembly = componentInterface.Assembly;

            var allComponentTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && componentInterface.IsAssignableFrom(t))
                .ToList();

            _persistenceMap = allComponentTypes.ToDictionary(
                t => t,
                t => t.IsDefined(typeof(PersistentAttribute), inherit: false));

            _nameMap = allComponentTypes.ToDictionary(
                t => t.FullName ?? t.Name,
                t => t);

            _persistentTypes = allComponentTypes
                .Where(t => _persistenceMap[t])
                .ToList()
                .AsReadOnly();
        }

        /// <inheritdoc/>
        public bool IsPersistent(Type componentType)
            => _persistenceMap.TryGetValue(componentType, out var persistent) && persistent;

        /// <inheritdoc/>
        public Type? Resolve(string typeName)
            => _nameMap.TryGetValue(typeName, out var type) ? type : null;

        /// <inheritdoc/>
        public IReadOnlyList<Type> AllPersistentTypes() => _persistentTypes;
    }
}
