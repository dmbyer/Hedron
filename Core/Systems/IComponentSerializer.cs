using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Serializes and deserializes individual <see cref="IComponent"/> instances to/from JSON.
    /// </summary>
    public interface IComponentSerializer
    {
        /// <summary>
        /// Serializes <paramref name="component"/> to a JSON string, using the component's
        /// runtime type so that all concrete properties are captured.
        /// </summary>
        string Serialize(IComponent component);

        /// <summary>
        /// Deserializes a component from <paramref name="data"/> (a JSON string) into the type
        /// identified by <paramref name="typeName"/> (full CLR type name).
        /// Returns <c>null</c> if the type cannot be resolved.
        /// </summary>
        IComponent? Deserialize(string typeName, string data);
    }
}
