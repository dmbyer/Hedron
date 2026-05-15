using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// YAML-backed <see cref="IContentSerializer"/>. Pure dispatcher — owns no domain
    /// knowledge of which kinds exist; modules register their own
    /// <see cref="ITemplateDeserializer"/>s via DI and this class fans out by kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in <c>Core/Systems/</c> as cross-cutting infrastructure. Per the architecture's
    /// 4-layer rule, core systems must not depend on domain modules — so the serializer no
    /// longer knows what a <c>RoomTemplate</c> or <c>AreaTemplate</c> is. Adding mob/item
    /// templates in future slices means registering a new <see cref="ITemplateDeserializer"/>
    /// in that module; this file is not edited.
    /// </para>
    /// <para>
    /// Persistence (slice 1) uses <c>System.Text.Json</c> on a separate code path —
    /// content authoring and runtime persistence do not share serializer code.
    /// </para>
    /// </remarks>
    public sealed class YamlContentSerializer : IContentSerializer
    {
        private readonly Dictionary<string, ITemplateDeserializer> _byKind;

        public string FormatExtension => ".yaml";

        public YamlContentSerializer(IEnumerable<ITemplateDeserializer> deserializers)
        {
            _byKind = deserializers.ToDictionary(d => d.Kind, StringComparer.OrdinalIgnoreCase);
        }

        public IEntityTemplate Deserialize(string kind, string fileBody)
        {
            if (!_byKind.TryGetValue(kind, out var deserializer))
                throw new ArgumentException(
                    $"No template deserializer registered for kind '{kind}'.", nameof(kind));
            return deserializer.Deserialize(fileBody);
        }
    }
}
