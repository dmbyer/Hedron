using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Effects
{
    public sealed class EffectsComponentJsonConverter : JsonConverter<EffectsComponent>
    {
        public override EffectsComponent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var effects = JsonSerializer.Deserialize<List<Effect>>(ref reader, options) ?? new List<Effect>();
            return new EffectsComponent { Effects = effects };
        }

        public override void Write(Utf8JsonWriter writer, EffectsComponent value, JsonSerializerOptions options)
        {
            var filtered = value.Effects.FindAll(e => e.Lifetime == EffectLifetime.UntilRemoved);
            JsonSerializer.Serialize(writer, filtered, options);
        }
    }
}
