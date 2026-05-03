namespace Hedron.Core.Systems
{
    /// <summary>
    /// Per-kind YAML → <see cref="IEntityTemplate"/> translator. One implementation per
    /// module that owns templated archetypes (rooms/areas live in the World module;
    /// future slices will register mob and item deserializers from their own modules).
    /// </summary>
    /// <remarks>
    /// This indirection is what keeps <see cref="IContentSerializer"/> module-agnostic.
    /// The concrete serializer (<see cref="YamlContentSerializer"/>) dispatches by
    /// <see cref="Kind"/>; modules wire their own deserializers via DI without the
    /// cross-cutting serializer ever needing to know they exist.
    /// </remarks>
    public interface ITemplateDeserializer
    {
        /// <summary>Top-level content discriminator (e.g. <c>"room"</c>, <c>"area"</c>).</summary>
        string Kind { get; }

        /// <summary>Translates a YAML file body into a concrete <see cref="IEntityTemplate"/>.</summary>
        IEntityTemplate Deserialize(string fileBody);
    }
}
