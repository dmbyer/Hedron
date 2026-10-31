using System;
using System.IO;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// The one place the shared YAML store builds its YamlDotNet (de)serializers and reads files.
    /// Builders are created per call because YamlDotNet's are not thread-safe (INV-31).
    /// </summary>
    internal static class YamlCodec
    {
        public static IDeserializer Deserializer() => new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        public static ISerializer Serializer(YamlCodecOptions options)
        {
            var builder = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithEventEmitter(next => new LiteralMultilineEmitter(next));
            if (options.OmitNulls)
                builder = builder.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull);
            return builder.Build();
        }

        // Multi-line strings (help-topic bodies) are written as literal `|` blocks, which read and
        // hand-edit exactly as authored; YamlDotNet's default folded style doubles every line break.
        private sealed class LiteralMultilineEmitter : ChainedEventEmitter
        {
            public LiteralMultilineEmitter(IEventEmitter next) : base(next) { }

            public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
            {
                if (eventInfo.Source.Value is string text && text.Contains('\n'))
                    eventInfo.Style = ScalarStyle.Literal;
                base.Emit(eventInfo, emitter);
            }
        }

        public static YamlReadResult<TDto> Read<TDto>(string path) where TDto : class
        {
            if (!File.Exists(path))
                return new YamlReadResult<TDto>(null, Exists: false, Error: null);

            try
            {
                var dto = Deserializer().Deserialize<TDto?>(ReadAllText(path));
                return new YamlReadResult<TDto>(dto, Exists: true, Error: null);
            }
            catch (Exception ex)
            {
                return new YamlReadResult<TDto>(null, Exists: true, Error: ex.Message, Fault: ex);
            }
        }

        // FileShare.ReadWrite | Delete, not File.ReadAllText: AtomicFileWrite's File.Replace only
        // succeeds while a reader holds the destination if that reader permitted deletion
        // (the same rule ContentFileReader documents).
        private static string ReadAllText(string path)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
