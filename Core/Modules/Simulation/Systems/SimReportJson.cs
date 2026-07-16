using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// The one <see cref="JsonSerializerOptions"/> instance every report producer/consumer shares
    /// (INV-19 applied to serialization) — extracted from <see cref="SimReportWriter"/> so
    /// <see cref="SimReportReader"/> deserializes with the identical camelCase/enum convention
    /// rather than forking a second dialect.
    /// </summary>
    internal static class SimReportJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
    }
}
