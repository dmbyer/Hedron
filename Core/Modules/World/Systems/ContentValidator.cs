using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Default <see cref="IContentValidator"/>. Holds the referential-integrity rules that used
    /// to live inline in <c>RegistryValidationBootstrap.StartAsync</c>, now callable on demand.
    /// </summary>
    public sealed class ContentValidator : IContentValidator
    {
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IAspectRegistry _aspectRegistry;
        private readonly EntityService _entityService;

        public ContentValidator(
            IAbilityRegistry abilityRegistry,
            IEffectRegistry effectRegistry,
            IAspectRegistry aspectRegistry,
            EntityService entityService)
        {
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
            _aspectRegistry = aspectRegistry;
            _entityService = entityService;
        }

        public ValidationReport ValidateRegistry(IReadOnlyCollection<string> startingAbilityIds)
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
                    ValidateAspectComposition(def.Aspect, $"Ability '{abilityId}'", errors, checkKeysRegistered: true);
            }

            // 2. StartingAbilities config → ability cross-refs.
            foreach (var abilityId in startingAbilityIds)
            {
                if (!string.IsNullOrWhiteSpace(abilityId) && !_abilityRegistry.TryGet(abilityId, out _))
                    errors.Add($"CharacterDefaults:StartingAbilities: ability '{abilityId}' not found in AbilityRegistry.");
            }

            // 3. Area entity AspectAffinities composition validation.
            foreach (var (entityId, _) in _entityService.GetAllComponents<AreaComponent>())
            {
                if (!_entityService.TryGet<AspectAffinitiesComponent>(entityId, out var affinities))
                    continue;

                // Parity with the original boot sweep: area affinities are validated for
                // normalization only, not aspect-key registration.
                var composition = new AspectComposition(affinities.AffinityWeights);
                ValidateAspectComposition(composition, $"Area entity {entityId}", errors, checkKeysRegistered: false);
            }

            return errors.Count == 0 ? ValidationReport.Ok : new ValidationReport(errors);
        }

        public ValidationReport Validate(IEntityTemplate template)
        {
            // Type-checking a template DTO (a plain authored data object) is not an entity type
            // check — INV-4 governs live-entity identity, not blueprint POCOs. Each kind applies
            // whatever single-definition rules it has; kinds with none return a valid report.
            if (template is AreaTemplate area && area.AspectAffinities is { Count: > 0 })
            {
                // Same rule as the boot area scan: normalization only (parity).
                var errors = new List<string>();
                var composition = new AspectComposition(area.AspectAffinities);
                ValidateAspectComposition(composition, $"Area '{area.BlueprintId}'", errors, checkKeysRegistered: false);
                return errors.Count == 0 ? ValidationReport.Ok : new ValidationReport(errors);
            }

            return ValidationReport.Ok;
        }

        private void ValidateAspectComposition(
            AspectComposition composition, string subject, List<string> errors, bool checkKeysRegistered)
        {
            if (!composition.IsValid(out var compError))
            {
                errors.Add($"{subject}: Aspect composition invalid — {compError}");
                return;
            }

            if (!checkKeysRegistered)
                return;

            foreach (var aspectId in composition.Weights.Keys)
            {
                if (!_aspectRegistry.TryGet(aspectId, out _))
                    errors.Add($"{subject}: Aspect key '{aspectId}' not found in AspectRegistry.");
            }
        }
    }
}
