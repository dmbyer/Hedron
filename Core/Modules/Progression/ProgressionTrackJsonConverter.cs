using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// Renders <see cref="ProgressionTrack"/> as its <see cref="ProgressionTrack.ToKey"/> string.
    ///
    /// <para>
    /// Attached by <c>[JsonConverter]</c> <b>on the struct</b> rather than registered in
    /// <c>ComponentSerializer.Options</c> — that field is <c>private static</c>, so a converter
    /// cannot be injected into it.
    /// </para>
    ///
    /// <para>
    /// <see cref="WriteAsPropertyName"/> / <see cref="ReadAsPropertyName"/> are the load-bearing
    /// overrides: <see cref="ProgressionTrack"/> is used as a <b>dictionary key</b> on
    /// <c>ProgressionComponent</c>, and <c>System.Text.Json</c> routes key serialization through
    /// those methods, not through <see cref="Write"/>/<see cref="Read"/>. Score tracks therefore
    /// keep emitting the bare enum name that pre-widening snapshots already contain.
    /// </para>
    /// </summary>
    public sealed class ProgressionTrackJsonConverter : JsonConverter<ProgressionTrack>
    {
        public override ProgressionTrack Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString());

        public override void Write(Utf8JsonWriter writer, ProgressionTrack value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToKey());

        public override ProgressionTrack ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriter writer, ProgressionTrack value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToKey());

        private static ProgressionTrack Parse(string? key)
        {
            if (!ProgressionTrack.TryParse(key, out var track))
                throw new JsonException($"'{key}' is not a valid progression track key.");

            return track;
        }
    }
}
