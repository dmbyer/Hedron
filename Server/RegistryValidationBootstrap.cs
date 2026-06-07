using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Effects;
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
    /// Validates: ability→effect cross-refs; ability→aspect cross-refs; aspect composition
    /// normalization (empty or sums to 100); StartingAbilities config → ability cross-refs.
    /// On any failure: logs a full report and throws, aborting boot (INV-10, OQ5).
    /// On success: publishes nothing — closed mechanical sweep (INV-10).
    /// </para>
    /// </summary>
    public sealed class RegistryValidationBootstrap : IHostedService
    {
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IAspectRegistry _aspectRegistry;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RegistryValidationBootstrap> _logger;

        public RegistryValidationBootstrap(
            IAbilityRegistry abilityRegistry,
            IEffectRegistry effectRegistry,
            IAspectRegistry aspectRegistry,
            IConfiguration configuration,
            ILogger<RegistryValidationBootstrap> logger)
        {
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
            _aspectRegistry = aspectRegistry;
            _configuration = configuration;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            // 1. Ability → effect + aspect cross-refs and composition normalization.
            foreach (var abilityId in _abilityRegistry.AllIds)
            {
                if (!_abilityRegistry.TryGet(abilityId, out var def))
                    continue;

                foreach (var effectId in def.Effects)
                {
                    if (!_effectRegistry.TryGet(effectId, out _))
                        errors.Add($"Ability '{abilityId}': effect '{effectId}' not found in EffectRegistry.");
                }

                if (def.Aspect != null)
                {
                    if (!def.Aspect.IsValid(out var compError))
                        errors.Add($"Ability '{abilityId}': Aspect composition invalid — {compError}");
                    else
                    {
                        foreach (var aspectId in def.Aspect.Weights.Keys)
                        {
                            if (!_aspectRegistry.TryGet(aspectId, out _))
                                errors.Add($"Ability '{abilityId}': Aspect key '{aspectId}' not found in AspectRegistry.");
                        }
                    }
                }
            }

            // 2. StartingAbilities config → ability cross-refs.
            var startingAbilities = _configuration
                .GetSection("CharacterDefaults:StartingAbilities")
                .Get<string[]>() ?? Array.Empty<string>();

            foreach (var abilityId in startingAbilities)
            {
                if (!string.IsNullOrWhiteSpace(abilityId) && !_abilityRegistry.TryGet(abilityId, out _))
                    errors.Add($"CharacterDefaults:StartingAbilities: ability '{abilityId}' not found in AbilityRegistry.");
            }

            if (errors.Count > 0)
            {
                var report = new StringBuilder();
                report.AppendLine($"Registry validation failed — {errors.Count} error(s):");
                foreach (var e in errors)
                    report.AppendLine($"  • {e}");

                var message = report.ToString();
                _logger.LogCritical("{Report}", message);
                throw new InvalidOperationException(message);
            }

            _logger.LogInformation(
                "RegistryValidationBootstrap: all cross-refs valid " +
                "({Abilities} abilities, {Effects} effects, {Aspects} aspects).",
                _abilityRegistry.AllIds.Count,
                _effectRegistry.AllIds.Count,
                _aspectRegistry.AllIds.Count);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
