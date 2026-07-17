using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Stats;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// YAML-backed <see cref="ISimScenarioStore"/>. The known-policy-id set comes from the
    /// DI-collected <see cref="ISimCombatantPolicy"/> registrants (never a hardcoded list), so a
    /// new built-in policy is automatically a valid <c>policyId</c> reference with no store change.
    /// </summary>
    public sealed class SimScenarioStore : ISimScenarioStore
    {
        private readonly IReadOnlyCollection<string> _knownPolicyIds;
        private readonly string _scenarioDirectory;
        private readonly IDeserializer _yamlDeserializer;
        private readonly ISerializer _yamlSerializer;

        public SimScenarioStore(IEnumerable<ISimCombatantPolicy> policies, IOptions<SimulationOptions> options)
        {
            _knownPolicyIds = policies.Select(p => p.PolicyId).ToList();
            _scenarioDirectory = options.Value.ScenarioDirectory;
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            _yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public ScenarioDefinition Load(string path, int? seedOverride = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"scenario file not found at '{path}'.");

            var body = File.ReadAllText(path);
            var dto = _yamlDeserializer.Deserialize<ScenarioFileDto>(body)
                ?? throw new InvalidOperationException("scenario YAML is empty.");

            var errors = new List<string>();

            if (!Enum.TryParse<ScenarioKind>(dto.Kind, ignoreCase: true, out var kind))
            {
                errors.Add($"unknown scenario kind '{dto.Kind}'.");
                kind = ScenarioKind.Combat;
            }

            var sides = new List<ScenarioSide>();
            if (dto.Sides is not null)
            {
                foreach (var sideDto in dto.Sides)
                {
                    var combatants = new List<CombatantSpec>();
                    if (sideDto.Combatants is not null)
                    {
                        foreach (var combatantDto in sideDto.Combatants)
                            combatants.Add(ToCombatantSpec(combatantDto, errors));
                    }
                    sides.Add(new ScenarioSide(combatants));
                }
            }

            ProgressionSettings? progression = null;
            if (dto.Progression is not null)
            {
                if (!Enum.TryParse<ScoreId>(dto.Progression.TargetTrack, ignoreCase: true, out var targetTrack))
                    errors.Add($"progression.targetTrack: unknown score id '{dto.Progression.TargetTrack}'.");

                progression = new ProgressionSettings(
                    targetTrack,
                    dto.Progression.TargetImprovements,
                    dto.Progression.MaxKillsPerRun,
                    dto.Progression.TicksPerKill);
            }

            var scenario = new ScenarioDefinition(
                kind,
                dto.Name ?? string.Empty,
                seedOverride ?? dto.Seed,
                dto.Iterations,
                dto.MaxTicksPerRun,
                sides,
                progression);

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"scenario '{path}' failed validation:\n" + string.Join("\n", errors.Select(e => "  • " + e)));

            Validate(scenario);
            return scenario;
        }

        public void Validate(ScenarioDefinition scenario)
        {
            var errors = new List<string>();

            if (scenario.Iterations <= 0)
                errors.Add($"iterations must be > 0 (was {scenario.Iterations}).");
            if (scenario.MaxTicksPerRun <= 0)
                errors.Add($"maxTicksPerRun must be > 0 (was {scenario.MaxTicksPerRun}).");

            // Both shipped kinds are 1v1 (subject/victim for progressionRate, side A/B for combat) —
            // enumerated explicitly rather than blanket-enforced so a future N-vs-N kind opts in.
            var requiresOneVOne = scenario.Kind is ScenarioKind.Combat or ScenarioKind.ProgressionRate;

            if (scenario.Sides.Count == 0)
                errors.Add("scenario must have at least one side.");
            else if (requiresOneVOne && scenario.Sides.Count != 2)
                errors.Add($"{scenario.Kind} scenarios require exactly 2 sides (had {scenario.Sides.Count}).");

            foreach (var (side, index) in scenario.Sides.Select((s, i) => (s, i)))
            {
                var label = $"sides[{index}]";
                if (side.Combatants.Count == 0)
                    errors.Add($"{label}: side has no combatants.");
                else if (requiresOneVOne && side.Combatants.Count != 1)
                    errors.Add($"{label}: {scenario.Kind} scenarios require exactly 1 combatant per side (had {side.Combatants.Count}).");

                foreach (var (combatant, cIndex) in side.Combatants.Select((c, i) => (c, i)))
                    ValidateCombatant(combatant, $"{label}.combatants[{cIndex}]", scenario.Kind, errors);
            }

            ValidateProgressionSection(scenario, errors);

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"scenario '{scenario.Name}' failed validation:\n" + string.Join("\n", errors.Select(e => "  • " + e)));
        }

        private static void ValidateProgressionSection(ScenarioDefinition scenario, List<string> errors)
        {
            if (scenario.Kind != ScenarioKind.ProgressionRate)
            {
                if (scenario.Progression is not null)
                    errors.Add($"'progression' section is only valid for progressionRate scenarios (kind was {scenario.Kind}).");
                return;
            }

            if (scenario.Progression is null)
            {
                errors.Add("progressionRate scenarios require a 'progression' section.");
                return;
            }

            var p = scenario.Progression;
            if (!ProgressionConstants.CombatTracks.Contains(p.TargetTrack))
                errors.Add(
                    $"progression.targetTrack '{p.TargetTrack}' is not a tracked combat track " +
                    $"({string.Join(", ", ProgressionConstants.CombatTracks)}).");
            if (p.TargetImprovements <= 0)
                errors.Add($"progression.targetImprovements must be > 0 (was {p.TargetImprovements}).");
            if (p.MaxKillsPerRun <= 0)
                errors.Add($"progression.maxKillsPerRun must be > 0 (was {p.MaxKillsPerRun}).");
            if (p.TicksPerKill is <= 0)
                errors.Add($"progression.ticksPerKill must be > 0 when present (was {p.TicksPerKill}).");
        }

        public async Task<string> SaveAsync(ScenarioDefinition scenario, CancellationToken ct = default)
        {
            Validate(scenario);

            Directory.CreateDirectory(_scenarioDirectory);

            var fileName = Sanitize(scenario.Name) + ".yaml";
            var path = Path.Combine(_scenarioDirectory, fileName);

            var body = _yamlSerializer.Serialize(ToDto(scenario));

            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, body, ct).ConfigureAwait(false);
            File.Move(tmpPath, path, overwrite: true);

            return path;
        }

        public IReadOnlyList<ScenarioFileSummary> List()
        {
            if (!Directory.Exists(_scenarioDirectory))
                return Array.Empty<ScenarioFileSummary>();

            var summaries = new List<ScenarioFileSummary>();
            foreach (var path in Directory.EnumerateFiles(_scenarioDirectory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var dto = _yamlDeserializer.Deserialize<ScenarioFileDto>(File.ReadAllText(path));
                    if (!string.IsNullOrWhiteSpace(dto?.Name))
                        name = dto!.Name!;
                }
                catch
                {
                    // Unparseable file: fall back to the filename-derived name (list, never throw).
                }

                summaries.Add(new ScenarioFileSummary(path, Path.GetFileName(path), name));
            }

            return summaries;
        }

        private static string CamelCase(string pascalCase) =>
            pascalCase.Length == 0 ? pascalCase : char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "scenario";

            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
        }

        private static ScenarioFileDto ToDto(ScenarioDefinition scenario) => new()
        {
            Kind = scenario.Kind.ToString(),
            Name = scenario.Name,
            Seed = scenario.Seed,
            Iterations = scenario.Iterations,
            MaxTicksPerRun = scenario.MaxTicksPerRun,
            Sides = scenario.Sides.Select(side => new SideDto
            {
                Combatants = side.Combatants.Select(ToDto).ToList(),
            }).ToList(),
            Progression = scenario.Progression is null ? null : new ProgressionDto
            {
                TargetTrack = CamelCase(scenario.Progression.TargetTrack.ToString()),
                TargetImprovements = scenario.Progression.TargetImprovements,
                MaxKillsPerRun = scenario.Progression.MaxKillsPerRun,
                TicksPerKill = scenario.Progression.TicksPerKill,
            },
        };

        private static CombatantDto ToDto(CombatantSpec combatant) => new()
        {
            Source = combatant.Source.ToString(),
            PolicyId = combatant.PolicyId,
            MobBlueprintId = combatant.MobBlueprintId,
            Tier = combatant.Tier,
            Band = combatant.Band,
            Inline = combatant.Inline is null
                ? null
                : new InlineDto
                {
                    Scores = combatant.Inline.Scores.ToDictionary(kv => CamelCase(kv.Key.ToString()), kv => kv.Value),
                    AbilityKit = combatant.Inline.AbilityKit.ToList(),
                },
        };

        private void ValidateCombatant(CombatantSpec combatant, string label, ScenarioKind kind, List<string> errors)
        {
            // Policy id selects an action-choice strategy — meaningless for a kind that never
            // executes actions (progressionRate). Combat-only, per the scenario-shape design note.
            if (kind == ScenarioKind.Combat && !_knownPolicyIds.Contains(combatant.PolicyId))
                errors.Add($"{label}: unknown policy id '{combatant.PolicyId}'.");

            switch (combatant.Source)
            {
                case CombatantSourceKind.MobTemplate:
                    if (string.IsNullOrWhiteSpace(combatant.MobBlueprintId))
                        errors.Add($"{label}: mobTemplate source requires mobBlueprintId.");
                    break;
                case CombatantSourceKind.ReferenceBuild:
                    if (combatant.Tier is null || combatant.Band is null)
                        errors.Add($"{label}: referenceBuild source requires tier and band.");
                    break;
                case CombatantSourceKind.Inline:
                    if (combatant.Inline is null)
                        errors.Add($"{label}: inline source requires an inline stat block.");
                    break;
                default:
                    errors.Add($"{label}: unknown combatant source discriminator.");
                    break;
            }
        }

        private static CombatantSpec ToCombatantSpec(CombatantDto dto, List<string> errors)
        {
            if (!Enum.TryParse<CombatantSourceKind>(dto.Source, ignoreCase: true, out var source))
            {
                errors.Add($"unknown combatant source '{dto.Source}'.");
                source = CombatantSourceKind.Inline;
            }

            InlineStatBlock? inline = null;
            if (dto.Inline is not null)
            {
                var scores = new Dictionary<ScoreId, int>();
                if (dto.Inline.Scores is not null)
                {
                    foreach (var (key, value) in dto.Inline.Scores)
                    {
                        if (Enum.TryParse<ScoreId>(key, ignoreCase: true, out var score))
                            scores[score] = value;
                        else
                            errors.Add($"inline.scores: unknown score id '{key}'.");
                    }
                }
                inline = new InlineStatBlock(scores, dto.Inline.AbilityKit ?? new List<string>());
            }

            return new CombatantSpec(
                source,
                dto.PolicyId ?? string.Empty,
                dto.MobBlueprintId,
                dto.Tier,
                dto.Band,
                inline);
        }

        // ── YAML DTOs (camelCase, same convention as content/standards files) ────────

        private sealed class ScenarioFileDto
        {
            public string? Kind { get; set; }
            public string? Name { get; set; }
            public int Seed { get; set; }
            public int Iterations { get; set; }
            public int MaxTicksPerRun { get; set; }
            public List<SideDto>? Sides { get; set; }
            public ProgressionDto? Progression { get; set; }
        }

        private sealed class ProgressionDto
        {
            public string? TargetTrack { get; set; }
            public int TargetImprovements { get; set; }
            public int MaxKillsPerRun { get; set; }
            public double? TicksPerKill { get; set; }
        }

        private sealed class SideDto
        {
            public List<CombatantDto>? Combatants { get; set; }
        }

        private sealed class CombatantDto
        {
            public string? Source { get; set; }
            public string? PolicyId { get; set; }
            public string? MobBlueprintId { get; set; }
            public int? Tier { get; set; }
            public int? Band { get; set; }
            public InlineDto? Inline { get; set; }
        }

        private sealed class InlineDto
        {
            public Dictionary<string, int>? Scores { get; set; }
            public List<string>? AbilityKit { get; set; }
        }
    }
}
