using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Simulation.Systems
{
    public sealed class SimReportReader : ISimReportReader
    {
        private readonly string _reportDirectory;

        public SimReportReader(IOptions<SimulationOptions> options)
        {
            _reportDirectory = options.Value.ReportDirectory;
        }

        public IReadOnlyList<SimReportSummary> List()
        {
            if (!Directory.Exists(_reportDirectory))
                return Array.Empty<SimReportSummary>();

            var summaries = new List<SimReportSummary>();
            foreach (var path in Directory.EnumerateFiles(_reportDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(path);
                try
                {
                    var report = ReadReport(path);
                    summaries.Add(new SimReportSummary(
                        path, fileName, Readable: true, report.GeneratedAt, report.Scenario.Name));
                }
                catch (Exception ex)
                {
                    summaries.Add(new SimReportSummary(path, fileName, Readable: false, Error: ex.Message));
                }
            }

            return summaries
                .OrderByDescending(s => s.GeneratedAt ?? DateTime.MinValue)
                .ThenByDescending(s => s.FileName, StringComparer.Ordinal)
                .ToList();
        }

        public SimulationReport Read(string path) => ReadReport(path);

        private static SimulationReport ReadReport(string path)
        {
            var body = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SimulationReport>(body, SimReportJson.Options)
                ?? throw new InvalidOperationException($"report '{path}' deserialized to null.");
        }
    }
}
