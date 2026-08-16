using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Account;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// YAML-based <see cref="IBalanceStandardsStore"/>. Domain-tier (BalanceInspection) — legally
    /// imports the <c>Ascension</c>/<c>Account</c> modules to run the mirror-drift comparison and
    /// <c>Abilities</c> to validate ability-kit ids, unlike the core-tier oracle itself (INV-2).
    /// </summary>
    public sealed class BalanceStandardsStore : IBalanceStandardsStore
    {
        private readonly string _path;
        private readonly IOptions<CharacterDefaultsOptions> _characterDefaults;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IDeserializer _yamlDeserializer;
        private readonly ISerializer _yamlSerializer;

        public BalanceStandardsStore(
            IOptions<BalanceOptions> options,
            IOptions<CharacterDefaultsOptions> characterDefaults,
            IAbilityRegistry abilityRegistry)
        {
            _path = options.Value.StandardsPath;
            _characterDefaults = characterDefaults;
            _abilityRegistry = abilityRegistry;
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            _yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public (BalanceStandardsDocument Document, IReadOnlyList<string> Warnings) Load()
        {
            var errors = new List<string>();

            BalanceStandardsDocument document;
            if (!File.Exists(_path))
            {
                document = BalanceStandardsDefaults.Document;
            }
            else
            {
                var body = File.ReadAllText(_path);
                var dto = _yamlDeserializer.Deserialize<StandardsFileDto>(body) ?? new StandardsFileDto();
                document = ToDomain(dto, errors);
            }

            ValidateStructural(document, errors);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Balance standards file '{_path}' failed validation:\n" +
                    string.Join("\n", errors.Select(e => "  • " + e)));
            }

            var warnings = new List<string>();
            CollectDriftWarnings(document, warnings);
            CollectAbilityKitWarnings(document, warnings);

            return (document, warnings);
        }

        public async Task<BalanceStandardsSaveResult> SaveAsync(BalanceStandardsDocument document, CancellationToken ct = default)
        {
            var errors = new List<string>();
            ValidateStructural(document, errors);
            if (errors.Count > 0)
                return new BalanceStandardsSaveResult(false, errors, Array.Empty<string>());

            var warnings = new List<string>();
            CollectDriftWarnings(document, warnings);
            CollectAbilityKitWarnings(document, warnings);

            var dto = ToDto(document);
            var body = _yamlSerializer.Serialize(dto);

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await AtomicFileWrite.ReplaceAsync(_path, body, ct).ConfigureAwait(false);

            return new BalanceStandardsSaveResult(true, Array.Empty<string>(), warnings);
        }

        // ── Structural validation (shared by Load and SaveAsync) ──────────────────

        private static void ValidateStructural(BalanceStandardsDocument document, List<string> errors)
        {
            var tunables = document.Tunables;

            if (tunables.BandsPerTier < 1)
                errors.Add($"tunables.bandsPerTier must be >= 1 (was {tunables.BandsPerTier}).");
            if (tunables.MaxTier < 0)
                errors.Add($"tunables.maxTier must be >= 0 (was {tunables.MaxTier}).");

            if (tunables.BandsPerTier >= 1)
            {
                var tierSpan = tunables.TierSpan();
                var thirdStep = tierSpan / tunables.BandsPerTier;
                if (tunables.BandSpan >= thirdStep)
                {
                    errors.Add(
                        $"tunables.bandSpan ({tunables.BandSpan}) must be strictly below " +
                        $"tierSpan/bandsPerTier ({tierSpan}/{tunables.BandsPerTier} = {thirdStep}) or the " +
                        "band subdivision stops being strictly ordered.");
                }
            }

            if (document.BandDriftTolerance < 0)
                errors.Add($"bandDriftTolerance must be >= 0 (was {document.BandDriftTolerance}).");

            ValidateTolerances(document.Outcomes, "outcomes", errors);

            var seenCells = new HashSet<(int Tier, int Band)>();
            foreach (var cell in document.Cells)
            {
                var label = $"cell (tier {cell.Tier}, band {cell.Band})";

                if (cell.Tier < 0 || cell.Tier > tunables.MaxTier)
                    errors.Add($"{label}: tier out of range 0-{tunables.MaxTier}.");
                if (cell.Band < 1 || cell.Band > tunables.BandsPerTier)
                    errors.Add($"{label}: band out of range 1-{tunables.BandsPerTier}.");
                if (!seenCells.Add((cell.Tier, cell.Band)))
                    errors.Add($"{label}: duplicate cell.");
                if (cell.OutcomesOverride is not null)
                    ValidateTolerances(cell.OutcomesOverride, $"{label}.outcomes", errors);
            }
        }

        private static void ValidateTolerances(OutcomeTolerances tolerances, string label, List<string> errors)
        {
            if (tolerances.EqualCellWinRate < 0)
                errors.Add($"{label}.equalCellWinRate must be >= 0 (was {tolerances.EqualCellWinRate}).");
            if (tolerances.WinRateTolerance < 0)
                errors.Add($"{label}.winRateTolerance must be >= 0 (was {tolerances.WinRateTolerance}).");
            if (tolerances.HigherBandWinRateFloor < 0)
                errors.Add($"{label}.higherBandWinRateFloor must be >= 0 (was {tolerances.HigherBandWinRateFloor}).");
        }

        // ── Mirror-drift + ability-kit warnings (never block) ─────────────────────

        private void CollectDriftWarnings(BalanceStandardsDocument document, List<string> warnings)
        {
            var tunables = document.Tunables;

            if (tunables.MaxTier != AscensionConstants.MaxTier)
            {
                warnings.Add(
                    $"tunables.maxTier ({tunables.MaxTier}) drifted from AscensionConstants.MaxTier " +
                    $"({AscensionConstants.MaxTier}).");
            }

            if (tunables.TierBaselineStep != AscensionConstants.TierBaselineStep)
            {
                warnings.Add(
                    $"tunables.tierBaselineStep ({tunables.TierBaselineStep}) drifted from " +
                    $"AscensionConstants.TierBaselineStep ({AscensionConstants.TierBaselineStep}).");
            }

            if (!SameScores(tunables.TrackedScores, AscensionConstants.TrackedScores))
            {
                warnings.Add(
                    $"tunables.trackedScores ([{string.Join(", ", tunables.TrackedScores)}]) drifted from " +
                    $"AscensionConstants.TrackedScores ([{string.Join(", ", AscensionConstants.TrackedScores)}]).");
            }

            var expectedBase = ExpectedReferenceBaseScores(_characterDefaults.Value);
            foreach (var (score, expected) in expectedBase)
            {
                var hasActual = tunables.ReferenceBaseScores.TryGetValue(score, out var actual);
                if (!hasActual || actual != expected)
                {
                    warnings.Add(
                        $"tunables.referenceBaseScores[{score}] " +
                        $"({(hasActual ? actual.ToString() : "absent")}) drifted from the " +
                        $"CharacterDefaultsOptions-derived value ({expected}).");
                }
            }
        }

        private void CollectAbilityKitWarnings(BalanceStandardsDocument document, List<string> warnings)
        {
            foreach (var cell in document.Cells)
            {
                foreach (var abilityId in cell.ReferenceBuild.AbilityKit)
                {
                    if (!_abilityRegistry.TryGet(abilityId, out _))
                    {
                        warnings.Add(
                            $"cell (tier {cell.Tier}, band {cell.Band}): unknown ability id " +
                            $"'{abilityId}' in abilityKit.");
                    }
                }
            }
        }

        private static bool SameScores(IReadOnlyList<ScoreId> a, IReadOnlyList<ScoreId> b)
            => a.Count == b.Count && !a.Except(b).Any();

        // Mirrors the pre-slice PowerBudgetConstants.ReferenceBaseScores comment: attributes from
        // CharacterDefaultsOptions.AttributeDefault, pools from Max{Hp,Mana,Stamina,Astra}, plus
        // the same base derivations IStatSystem uses (AttackPower = Body/2, Defense = Body/4).
        private static Dictionary<ScoreId, int> ExpectedReferenceBaseScores(CharacterDefaultsOptions defaults)
        {
            var body = defaults.AttributeDefault;
            return new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = defaults.AttributeDefault,
                [ScoreId.Body] = body,
                [ScoreId.Spirit] = defaults.AttributeDefault,
                [ScoreId.Attunement] = defaults.AttributeDefault,
                [ScoreId.HpMax] = defaults.MaxHp,
                [ScoreId.ManaMax] = defaults.MaxMana,
                [ScoreId.StaminaMax] = defaults.MaxStamina,
                [ScoreId.AstraMax] = defaults.MaxAstra,
                [ScoreId.AttackPower] = body / 2,
                [ScoreId.Defense] = body / 4,
            };
        }

        // ── DTO ⇄ domain conversion ────────────────────────────────────────────────

        private BalanceStandardsDocument ToDomain(StandardsFileDto dto, List<string> errors)
        {
            var defaults = PowerBudgetTunables.Default;
            var tunablesDto = dto.Tunables;

            // Weights/ReferenceBaseScores are merged over the compiled defaults, not replaced
            // wholesale — authoring one score's weight must not silently zero out every other
            // score's weight (and likewise for reference base scores).
            var weights = tunablesDto?.Weights is not null
                ? MergeScoreMap(defaults.Weights, tunablesDto.Weights, "tunables.weights", errors)
                : new Dictionary<ScoreId, int>(defaults.Weights);

            var referenceBaseScores = tunablesDto?.ReferenceBaseScores is not null
                ? MergeScoreMap(defaults.ReferenceBaseScores, tunablesDto.ReferenceBaseScores, "tunables.referenceBaseScores", errors)
                : new Dictionary<ScoreId, int>(defaults.ReferenceBaseScores);

            var trackedScores = tunablesDto?.TrackedScores is not null
                ? ParseScoreList(tunablesDto.TrackedScores, "tunables.trackedScores", errors)
                : new List<ScoreId>(defaults.TrackedScores);

            var tunables = new PowerBudgetTunables(
                Weights: weights,
                BandSpan: tunablesDto?.BandSpan ?? defaults.BandSpan,
                BandsPerTier: tunablesDto?.BandsPerTier ?? defaults.BandsPerTier,
                ReferenceBaseScores: referenceBaseScores,
                MaxTier: tunablesDto?.MaxTier ?? defaults.MaxTier,
                TierBaselineStep: tunablesDto?.TierBaselineStep ?? defaults.TierBaselineStep,
                TrackedScores: trackedScores);

            var outcomes = ToOutcomeTolerances(dto.Outcomes) ?? BalanceStandardsDefaults.Outcomes;

            var cells = new List<BalanceStandard>();
            if (dto.Cells is not null)
            {
                foreach (var cellDto in dto.Cells)
                {
                    var label = $"cell (tier {cellDto.Tier}, band {cellDto.Band}).gearBonuses";
                    var gearBonuses = cellDto.GearBonuses is not null
                        ? ParseScoreMap(cellDto.GearBonuses, label, errors)
                        : new Dictionary<ScoreId, int>();

                    var abilityKit = cellDto.AbilityKit is not null
                        ? new List<string>(cellDto.AbilityKit)
                        : new List<string>();

                    cells.Add(new BalanceStandard(
                        cellDto.Tier,
                        cellDto.Band,
                        new ReferenceBuildDefinition(gearBonuses, abilityKit),
                        ToOutcomeTolerances(cellDto.Outcomes)));
                }
            }

            return new BalanceStandardsDocument(
                tunables,
                dto.BandDriftTolerance ?? BalanceStandardsDefaults.BandDriftTolerance,
                outcomes,
                cells);
        }

        private static OutcomeTolerances? ToOutcomeTolerances(OutcomeTolerancesDto? dto)
        {
            if (dto is null)
                return null;

            return new OutcomeTolerances(
                dto.EqualCellWinRate ?? BalanceStandardsDefaults.Outcomes.EqualCellWinRate,
                dto.WinRateTolerance ?? BalanceStandardsDefaults.Outcomes.WinRateTolerance,
                dto.HigherBandWinRateFloor ?? BalanceStandardsDefaults.Outcomes.HigherBandWinRateFloor);
        }

        private static Dictionary<ScoreId, int> ParseScoreMap(
            Dictionary<string, int> raw, string label, List<string> errors)
        {
            var result = new Dictionary<ScoreId, int>();
            foreach (var (key, value) in raw)
            {
                if (Enum.TryParse<ScoreId>(key, ignoreCase: true, out var score))
                    result[score] = value;
                else
                    errors.Add($"{label}: unknown score id '{key}'.");
            }
            return result;
        }

        // Like ParseScoreMap, but starts from a copy of the compiled defaults and overlays only
        // the authored entries — sparse authoring of one score must not drop every other score.
        private static Dictionary<ScoreId, int> MergeScoreMap(
            IReadOnlyDictionary<ScoreId, int> defaults, Dictionary<string, int> raw, string label, List<string> errors)
        {
            var result = new Dictionary<ScoreId, int>(defaults);
            foreach (var (key, value) in raw)
            {
                if (Enum.TryParse<ScoreId>(key, ignoreCase: true, out var score))
                    result[score] = value;
                else
                    errors.Add($"{label}: unknown score id '{key}'.");
            }
            return result;
        }

        private static List<ScoreId> ParseScoreList(List<string> raw, string label, List<string> errors)
        {
            var result = new List<ScoreId>();
            foreach (var name in raw)
            {
                if (Enum.TryParse<ScoreId>(name, ignoreCase: true, out var score))
                    result.Add(score);
                else
                    errors.Add($"{label}: unknown score id '{name}'.");
            }
            return result;
        }

        private static StandardsFileDto ToDto(BalanceStandardsDocument document)
        {
            return new StandardsFileDto
            {
                Tunables = new TunablesDto
                {
                    Weights = ToScoreMapDto(document.Tunables.Weights),
                    BandSpan = document.Tunables.BandSpan,
                    BandsPerTier = document.Tunables.BandsPerTier,
                    ReferenceBaseScores = ToScoreMapDto(document.Tunables.ReferenceBaseScores),
                    MaxTier = document.Tunables.MaxTier,
                    TierBaselineStep = document.Tunables.TierBaselineStep,
                    TrackedScores = document.Tunables.TrackedScores.Select(ScoreName).ToList(),
                },
                BandDriftTolerance = document.BandDriftTolerance,
                Outcomes = ToOutcomeTolerancesDto(document.Outcomes),
                Cells = document.Cells.Select(cell => new CellDto
                {
                    Tier = cell.Tier,
                    Band = cell.Band,
                    GearBonuses = ToScoreMapDto(cell.ReferenceBuild.GearBonuses),
                    AbilityKit = cell.ReferenceBuild.AbilityKit.Count > 0
                        ? new List<string>(cell.ReferenceBuild.AbilityKit)
                        : null,
                    Outcomes = cell.OutcomesOverride is not null
                        ? ToOutcomeTolerancesDto(cell.OutcomesOverride)
                        : null,
                }).ToList(),
            };
        }

        private static OutcomeTolerancesDto ToOutcomeTolerancesDto(OutcomeTolerances tolerances) => new()
        {
            EqualCellWinRate = tolerances.EqualCellWinRate,
            WinRateTolerance = tolerances.WinRateTolerance,
            HigherBandWinRateFloor = tolerances.HigherBandWinRateFloor,
        };

        private static Dictionary<string, int> ToScoreMapDto(IReadOnlyDictionary<ScoreId, int> scores)
        {
            var result = new Dictionary<string, int>();
            foreach (var (score, value) in scores)
                result[ScoreName(score)] = value;
            return result;
        }

        private static string ScoreName(ScoreId score) => score.ToString().ToLowerInvariant();

        // ── YAML DTOs ──────────────────────────────────────────────────────────────

        private sealed class StandardsFileDto
        {
            public TunablesDto? Tunables { get; set; }
            public int? BandDriftTolerance { get; set; }
            public OutcomeTolerancesDto? Outcomes { get; set; }
            public List<CellDto>? Cells { get; set; }
        }

        private sealed class TunablesDto
        {
            public Dictionary<string, int>? Weights { get; set; }
            public int? BandSpan { get; set; }
            public int? BandsPerTier { get; set; }
            public Dictionary<string, int>? ReferenceBaseScores { get; set; }
            public int? MaxTier { get; set; }
            public int? TierBaselineStep { get; set; }
            public List<string>? TrackedScores { get; set; }
        }

        private sealed class OutcomeTolerancesDto
        {
            public double? EqualCellWinRate { get; set; }
            public double? WinRateTolerance { get; set; }
            public double? HigherBandWinRateFloor { get; set; }
        }

        private sealed class CellDto
        {
            public int Tier { get; set; }
            public int Band { get; set; }
            public Dictionary<string, int>? GearBonuses { get; set; }
            public List<string>? AbilityKit { get; set; }
            public OutcomeTolerancesDto? Outcomes { get; set; }
        }
    }
}
