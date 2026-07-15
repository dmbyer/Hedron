using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hedron.Core.Modules.Stats;
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
        private readonly IDeserializer _yamlDeserializer;

        public SimScenarioStore(IEnumerable<ISimCombatantPolicy> policies)
        {
            _knownPolicyIds = policies.Select(p => p.PolicyId).ToList();
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
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

            var scenario = new ScenarioDefinition(
                kind,
                dto.Name ?? string.Empty,
                seedOverride ?? dto.Seed,
                dto.Iterations,
                dto.MaxTicksPerRun,
                sides);

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

            if (scenario.Sides.Count == 0)
                errors.Add("scenario must have at least one side.");
            else if (scenario.Kind == ScenarioKind.Combat && scenario.Sides.Count != 2)
                errors.Add($"combat scenarios require exactly 2 sides (had {scenario.Sides.Count}).");

            foreach (var (side, index) in scenario.Sides.Select((s, i) => (s, i)))
            {
                var label = $"sides[{index}]";
                if (side.Combatants.Count == 0)
                    errors.Add($"{label}: side has no combatants.");
                else if (scenario.Kind == ScenarioKind.Combat && side.Combatants.Count != 1)
                    errors.Add($"{label}: combat scenarios require exactly 1 combatant per side (had {side.Combatants.Count}).");

                foreach (var (combatant, cIndex) in side.Combatants.Select((c, i) => (c, i)))
                    ValidateCombatant(combatant, $"{label}.combatants[{cIndex}]", errors);
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"scenario '{scenario.Name}' failed validation:\n" + string.Join("\n", errors.Select(e => "  • " + e)));
        }

        private void ValidateCombatant(CombatantSpec combatant, string label, List<string> errors)
        {
            if (!_knownPolicyIds.Contains(combatant.PolicyId))
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
