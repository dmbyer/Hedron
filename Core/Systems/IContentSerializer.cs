namespace Hedron.Core.Systems
{
    /// <summary>
    /// Designer-write content format. One implementation per format kind; this slice ships
    /// a YAML implementation. Persistence (slice 1) uses <c>System.Text.Json</c> on a
    /// separate code path — content authoring and runtime persistence do not share serializer
    /// code.
    /// </summary>
    public interface IContentSerializer
    {
        /// <summary>Deserializes a content file body into an <see cref="IEntityTemplate"/>.</summary>
        /// <param name="kind">
        /// Top-level content kind discriminator the loader expects (e.g. <c>"room"</c>, <c>"area"</c>).
        /// Determines which template POCO is constructed.
        /// </param>
        /// <param name="fileBody">Raw text contents of the YAML file.</param>
        IEntityTemplate Deserialize(string kind, string fileBody);

        /// <summary>File extension this serializer recognises (e.g. <c>".yaml"</c>).</summary>
        string FormatExtension { get; }
    }
}
