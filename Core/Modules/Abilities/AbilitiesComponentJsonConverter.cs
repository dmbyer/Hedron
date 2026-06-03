using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Abilities
{
    public sealed class AbilitiesComponentJsonConverter : JsonConverter<AbilitiesComponent>
    {
        public override AbilitiesComponent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var known = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? new List<string>();
            return new AbilitiesComponent { Known = known };
        }

        public override void Write(Utf8JsonWriter writer, AbilitiesComponent value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Known, options);
        }
    }
}
