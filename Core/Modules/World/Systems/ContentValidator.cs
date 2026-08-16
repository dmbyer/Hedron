using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Mobs.Templates;
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
        private static readonly Regex FilenameSafePattern =
            new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IAspectRegistry _aspectRegistry;
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;

        public ContentValidator(
            IAbilityRegistry abilityRegistry,
            IEffectRegistry effectRegistry,
            IAspectRegistry aspectRegistry,
            EntityService entityService,
            ITemplateRegistry templateRegistry)
        {
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
            _aspectRegistry = aspectRegistry;
            _entityService = entityService;
            _templateRegistry = templateRegistry;
        }

        public ValidationReport ValidateRegistry(IReadOnlyCollection<string> startingAbilityIds)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

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

            // 4. Coordinate-collision warning (warn-not-error): two rooms in one area sharing the
            // same X/Y/Z. Sourced from the template registry, not the live world — this is an
            // authoring-content sweep, not a live-entity check.
            var roomTemplates = _templateRegistry.AllBlueprintIds()
                .Select(id => _templateRegistry.TryGet(id, out var template) ? template : null)
                .OfType<RoomTemplate>();
            foreach (var collision in RoomCoordinateCollisions.Find(roomTemplates))
            {
                warnings.Add(
                    $"Coordinate collision: area '{collision.AreaId}' has {collision.RoomBlueprintIds.Count} " +
                    $"rooms at ({collision.X}, {collision.Y}, {collision.Z}): " +
                    string.Join(", ", collision.RoomBlueprintIds));
            }

            return new ValidationReport(errors, warnings);
        }

        public ValidationReport Validate(IEntityTemplate template)
        {
            // Type-checking a template DTO (a plain authored data object) is not an entity type
            // check — INV-4 governs live-entity identity, not blueprint POCOs. Each kind applies
            // whatever single-definition rules it has; kinds with none return a valid report.
            switch (template)
            {
                case AreaTemplate area when area.AspectAffinities is { Count: > 0 }:
                {
                    // Same rule as the boot area scan: normalization only (parity).
                    var errors = new List<string>();
                    var composition = new AspectComposition(area.AspectAffinities);
                    ValidateAspectComposition(composition, $"Area '{area.BlueprintId}'", errors, checkKeysRegistered: false);
                    return errors.Count == 0 ? ValidationReport.Ok : new ValidationReport(errors);
                }

                case MobTemplate mob:
                    return ValidateCurrencyLoot(mob);

                default:
                    return ValidationReport.Ok;
            }
        }

        /// <summary>
        /// A mob's authored currency-loot ranges must be well-formed: non-negative, and
        /// <c>Min ≤ Max</c>. An inverted range is not clamped or reinterpreted at spawn — the roll
        /// is undefined — so it is refused before the YAML is written rather than shipped as
        /// content that misbehaves on the next reload.
        /// </summary>
        private static ValidationReport ValidateCurrencyLoot(MobTemplate mob)
        {
            if (mob.CurrencyLoot.Count == 0)
                return ValidationReport.Ok;

            var errors = new List<string>();
            foreach (var (currency, range) in mob.CurrencyLoot)
            {
                if (range.Min < 0 || range.Max < 0)
                {
                    errors.Add(
                        $"Mob '{mob.BlueprintId}': {currency} loot range ({range.Min}, {range.Max}) " +
                        $"must not be negative.");
                }

                if (range.Min > range.Max)
                {
                    errors.Add(
                        $"Mob '{mob.BlueprintId}': {currency} loot minimum ({range.Min}) must not " +
                        $"exceed its maximum ({range.Max}).");
                }
            }

            return errors.Count == 0 ? ValidationReport.Ok : new ValidationReport(errors);
        }

        public ValidationReport ValidateBlueprintId(ContentKind kind, string blueprintId)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrEmpty(blueprintId))
            {
                errors.Add("Blueprint id must not be empty.");
                return new ValidationReport(errors, warnings);
            }

            if (blueprintId.IndexOf('/') >= 0 || blueprintId.IndexOf('\\') >= 0)
                errors.Add($"Blueprint id '{blueprintId}' must not contain a path separator.");

            if (blueprintId.Contains(".."))
                errors.Add($"Blueprint id '{blueprintId}' must not contain a '..' segment.");

            if (!FilenameSafePattern.IsMatch(blueprintId))
                errors.Add($"Blueprint id '{blueprintId}' must contain only letters, digits, '.', '_', or '-'.");

            if (ReservedWindowsNames.Contains(blueprintId))
                errors.Add($"Blueprint id '{blueprintId}' is a reserved device name.");

            var expectedPrefix = kind.KindString() + ".";
            if (!blueprintId.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Blueprint id '{blueprintId}' does not start with the conventional " +
                    $"'{expectedPrefix}' prefix for {kind} definitions. This is allowed — the " +
                    $"loader keys off the kind subdirectory, not the prefix.");
            }

            return new ValidationReport(errors, warnings);
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
