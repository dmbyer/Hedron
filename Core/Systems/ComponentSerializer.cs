using System.Text.Json;
using System.Text.Json.Serialization;
using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// <see cref="System.Text.Json"/>-backed implementation of <see cref="IComponentSerializer"/>.
    /// Uses camelCase naming and a <see cref="JsonStringEnumConverter"/> so that enum values
    /// round-trip as human-readable strings in entity snapshot files.
    /// </summary>
    public sealed class ComponentSerializer : IComponentSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly IComponentTypeRegistry _typeRegistry;

        public ComponentSerializer(IComponentTypeRegistry typeRegistry)
        {
            _typeRegistry = typeRegistry;
        }

        /// <inheritdoc/>
        public string Serialize(IComponent component)
            => JsonSerializer.Serialize(component, component.GetType(), Options);

        /// <inheritdoc/>
        public IComponent? Deserialize(string typeName, string data)
        {
            var type = _typeRegistry.Resolve(typeName);
            if (type is null)
                return null;

            return (IComponent?)JsonSerializer.Deserialize(data, type, Options);
        }
    }
}
