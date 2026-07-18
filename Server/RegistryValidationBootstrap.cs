using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.BalanceInspection.Standards;
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
    /// <para>
    /// Also forces eager DI resolution of <see cref="IBalanceStandardsRegistry"/>: that singleton
    /// factory is otherwise lazy, so without a forced resolve here a structurally invalid balance
    /// standards file would only surface on the first admin command/editor page hit rather than at
    /// boot (sim-1 Postcondition 4 — fail-fast at boot on both hosts, since this hosted service is
    /// registered by both <c>AddGameplayHostedServices</c> and <c>AddContentBootstrapHostedServices</c>).
    /// </para>
    /// </summary>
    public sealed class RegistryValidationBootstrap : IHostedService
    {
        private readonly IContentValidator _validator;
        private readonly IConfiguration _configuration;
        // Unused beyond forcing eager construction (see remarks) — resolving it here is what
        // makes a bad standards file fail boot instead of the first admin command/editor hit.
        private readonly IBalanceStandardsRegistry _balanceStandards;
        private readonly ILogger<RegistryValidationBootstrap> _logger;

        public RegistryValidationBootstrap(
            IContentValidator validator,
            IConfiguration configuration,
            IBalanceStandardsRegistry balanceStandards,
            ILogger<RegistryValidationBootstrap> logger)
        {
            _validator = validator;
            _configuration = configuration;
            _balanceStandards = balanceStandards;
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

            if (report.Warnings.Count > 0)
            {
                var builder = new StringBuilder();
                builder.AppendLine($"Registry validation — {report.Warnings.Count} warning(s):");
                foreach (var w in report.Warnings)
                    builder.AppendLine($"  • {w}");
                _logger.LogWarning("{Report}", builder.ToString());
            }

            _logger.LogInformation("RegistryValidationBootstrap: all content cross-refs valid.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
