using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Systems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hedron.Server
{
    /// <summary>
    /// Hosted service that runs a fail-fast referential-integrity sweep after registries
    /// are populated and world content is ready. Must be registered after
    /// <see cref="WorldContentBootstrap"/> in <c>Program.cs</c>.
    /// <para>
    /// The validation rules live in <see cref="IContentValidator"/> (callable on demand by the
    /// authoring editor and the bulk generator too). This bootstrap owns only the host policy:
    /// read the configured starting abilities, run the registry sweep, and on any failure log a
    /// full report and throw, aborting boot (INV-10). On success it publishes nothing — a closed
    /// mechanical sweep (INV-10).
    /// </para>
    /// </summary>
    public sealed class RegistryValidationBootstrap : IHostedService
    {
        private readonly IContentValidator _validator;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RegistryValidationBootstrap> _logger;

        public RegistryValidationBootstrap(
            IContentValidator validator,
            IConfiguration configuration,
            ILogger<RegistryValidationBootstrap> logger)
        {
            _validator = validator;
            _configuration = configuration;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var startingAbilities = _configuration
                .GetSection("CharacterDefaults:StartingAbilities")
                .Get<string[]>() ?? Array.Empty<string>();

            var report = _validator.ValidateRegistry(startingAbilities);

            if (!report.IsValid)
            {
                var builder = new StringBuilder();
                builder.AppendLine($"Registry validation failed — {report.Errors.Count} error(s):");
                foreach (var e in report.Errors)
                    builder.AppendLine($"  • {e}");

                var message = builder.ToString();
                _logger.LogCritical("{Report}", message);
                throw new InvalidOperationException(message);
            }

            _logger.LogInformation("RegistryValidationBootstrap: all content cross-refs valid.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
