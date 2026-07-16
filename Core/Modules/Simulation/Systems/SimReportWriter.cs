using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Simulation.Systems
{
    public sealed class SimReportWriter : ISimReportWriter
    {
        private readonly string _reportDirectory;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public SimReportWriter(IOptions<SimulationOptions> options)
        {
            _reportDirectory = options.Value.ReportDirectory;
        }

        public async Task<string> WriteAsync(SimulationReport report, CancellationToken ct = default)
        {
            Directory.CreateDirectory(_reportDirectory);

            var timestamp = report.GeneratedAt.ToString("yyyyMMdd-HHmmss") + "Z";
            var safeName = Sanitize(report.Scenario.Name);
            var fileName = $"{timestamp}-{safeName}-{report.Scenario.Seed}.json";
            var path = Path.Combine(_reportDirectory, fileName);

            var body = JsonSerializer.Serialize(report, SerializerOptions);

            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, path, overwrite: true);

            return path;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "scenario";

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
            return cleaned;
        }
    }
}
