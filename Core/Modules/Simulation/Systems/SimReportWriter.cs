using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    public sealed class SimReportWriter : ISimReportWriter
    {
        private readonly string _reportDirectory;

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

            var body = JsonSerializer.Serialize(report, SimReportJson.Options);

            await AtomicFileWrite.ReplaceAsync(path, body, ct).ConfigureAwait(false);

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
