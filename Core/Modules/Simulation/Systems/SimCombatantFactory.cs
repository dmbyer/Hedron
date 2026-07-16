using System;
using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Ascension.Components;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Resolves the three day-one combatant sources (mob template, standards reference build,
    /// inline stat block) and materializes them as mob-archetype sandbox entities. Gear-equivalent
    /// bonuses on the computed scores (<see cref="ScoreId.AttackPower"/>/<see cref="ScoreId.Defense"/>)
    /// fold through a synthetic permanent <c>StatModifier</c> effect — the same
    /// <see cref="Effects.Systems.IEffectSystem.Apply"/> seam worn gear rides — so
    /// <c>IStatSystem.Get</c> is the only path a value reaches the combatant (never a bypass write).
    /// </summary>
    public sealed class SimCombatantFactory : ISimCombatantFactory
    {
        private readonly IContentDefinitionCatalog _catalog;
        private readonly IBalanceStandardsRegistry _standardsRegistry;
        private readonly IAbilityRegistry _abilityRegistry;

        public SimCombatantFactory(
            IContentDefinitionCatalog catalog,
            IBalanceStandardsRegistry standardsRegistry,
            IAbilityRegistry abilityRegistry)
        {
            _catalog = catalog;
            _standardsRegistry = standardsRegistry;
            _abilityRegistry = abilityRegistry;
        }

        public ResolvedCombatant Resolve(CombatantSpec spec)
        {
            var resolved = spec.Source switch
            {
                CombatantSourceKind.MobTemplate => ResolveMobTemplate(spec),
                CombatantSourceKind.ReferenceBuild => ResolveReferenceBuild(spec),
                CombatantSourceKind.Inline => ResolveInline(spec),
                _ => throw new InvalidOperationException($"unknown combatant source '{spec.Source}'."),
            };

            foreach (var abilityId in resolved.AbilityKit)
            {
                if (!_abilityRegistry.TryGet(abilityId, out _))
                    throw new InvalidOperationException($"combatant '{resolved.Name}': unknown ability id '{abilityId}'.");
            }

            return resolved;
        }

        private ResolvedCombatant ResolveMobTemplate(CombatantSpec spec)
        {
            var definition = _catalog.Load(ContentKind.Mob, spec.MobBlueprintId!)
                ?? throw new InvalidOperationException($"unknown mob blueprint id '{spec.MobBlueprintId}'.");
            var template = definition.Template as MobTemplate
                ?? throw new InvalidOperationException($"blueprint '{spec.MobBlueprintId}' is not a mob template.");

            var scores = new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = template.Mind > 0 ? template.Mind : 10,
                [ScoreId.Body] = template.Body > 0 ? template.Body : 10,
                [ScoreId.Spirit] = template.Spirit > 0 ? template.Spirit : 10,
                [ScoreId.Attunement] = template.Attunement > 0 ? template.Attunement : 10,
                [ScoreId.HpMax] = template.MaxHp > 0 ? template.MaxHp : 100,
                [ScoreId.ManaMax] = template.MaxMana > 0 ? template.MaxMana : 50,
                [ScoreId.StaminaMax] = template.MaxStamina > 0 ? template.MaxStamina : 50,
                [ScoreId.AstraMax] = template.MaxAstra > 0 ? template.MaxAstra : 10,
            };

            var cell = template.Band >= 1 ? new PowerBand(template.Tier, template.Band) : (PowerBand?)null;

            return new ResolvedCombatant(
                template.Name, scores, Array.Empty<string>(), template.Tier, spec.PolicyId, cell);
        }

        private ResolvedCombatant ResolveReferenceBuild(CombatantSpec spec)
        {
            var tier = spec.Tier ?? throw new InvalidOperationException("referenceBuild source requires tier.");
            var band = spec.Band ?? throw new InvalidOperationException("referenceBuild source requires band.");

            var standard = _standardsRegistry.Get(new PowerBand(tier, band));
            var snapshot = _standardsRegistry.ReferenceSnapshot(tier, band);

            return new ResolvedCombatant(
                $"reference.t{tier}b{band}",
                snapshot.Scores,
                standard.ReferenceBuild.AbilityKit,
                tier,
                spec.PolicyId,
                new PowerBand(tier, band));
        }

        private static ResolvedCombatant ResolveInline(CombatantSpec spec)
        {
            var inline = spec.Inline
                ?? throw new InvalidOperationException("inline source requires an inline stat block.");

            foreach (var score in inline.Scores.Keys)
            {
                if (!Enum.IsDefined(typeof(ScoreId), score))
                    throw new InvalidOperationException($"inline stat block: unknown score id '{score}'.");
            }

            var cell = spec.Tier.HasValue && spec.Band.HasValue
                ? new PowerBand(spec.Tier.Value, spec.Band.Value)
                : (PowerBand?)null;

            return new ResolvedCombatant(
                "inline", inline.Scores, inline.AbilityKit, spec.Tier ?? 0, spec.PolicyId, cell);
        }

        public uint Materialize(SandboxWorld world, ResolvedCombatant resolved)
        {
            var entity = world.EntityService.CreateEntity();
            var entityId = entity.Id;

            world.EntityService.AddComponent(entityId, new MobDataComponent { Name = resolved.Name });

            var body = Score(resolved.Scores, ScoreId.Body, 10);
            world.EntityService.AddComponent(entityId, new AttributesComponent
            {
                Level = 1,
                Mind = Score(resolved.Scores, ScoreId.Mind, 10),
                Body = body,
                Spirit = Score(resolved.Scores, ScoreId.Spirit, 10),
                Attunement = Score(resolved.Scores, ScoreId.Attunement, 10),
            });

            var maxHp = Score(resolved.Scores, ScoreId.HpMax, 100);
            var maxMana = Score(resolved.Scores, ScoreId.ManaMax, 50);
            var maxStamina = Score(resolved.Scores, ScoreId.StaminaMax, 50);
            var maxAstra = Score(resolved.Scores, ScoreId.AstraMax, 10);
            world.EntityService.AddComponent(entityId, new PoolsComponent
            {
                MaxHp = maxHp,
                CurrentHp = maxHp,
                MaxMana = maxMana,
                CurrentMana = maxMana,
                MaxStamina = maxStamina,
                CurrentStamina = maxStamina,
                MaxAstra = maxAstra,
                CurrentAstra = maxAstra,
            });

            world.EntityService.AddComponent(entityId, new LocationComponent { RoomEntityId = world.ArenaRoomEntityId });

            if (resolved.Tier > 0)
                world.EntityService.AddComponent(entityId, new AscensionComponent { Tier = resolved.Tier });

            // Computed scores (AttackPower = Body/2, Defense = Body/4) aren't stored on a component —
            // any snapshot value beyond that base derivation folds as a permanent StatModifier effect,
            // exactly how worn-gear bonuses reach IStatSystem.Get. A resolved combatant with no such
            // score (e.g. a mob template) contributes a residual of 0 — a no-op.
            ApplyComputedResidual(world, entityId, ScoreId.AttackPower, resolved.Scores, body / 2);
            ApplyComputedResidual(world, entityId, ScoreId.Defense, resolved.Scores, body / 4);

            foreach (var abilityId in resolved.AbilityKit)
                world.Abilities.Learn(entityId, abilityId);

            return entityId;
        }

        private static void ApplyComputedResidual(
            SandboxWorld world, uint entityId, ScoreId score, IReadOnlyDictionary<ScoreId, int> scores, int baseValue)
        {
            if (!scores.TryGetValue(score, out var target))
                return;

            var residual = target - baseValue;
            if (residual == 0)
                return;

            var definition = new EffectDefinition(
                EffectId: $"sim.gear.{score}",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(score, residual),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: -1f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);

            world.Effects.Apply(entityId, definition, entityId);
        }

        private static int Score(IReadOnlyDictionary<ScoreId, int> scores, ScoreId score, int fallback) =>
            scores.TryGetValue(score, out var value) ? value : fallback;
    }
}
