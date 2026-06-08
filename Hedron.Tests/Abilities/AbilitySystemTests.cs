using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Abilities
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AbilitySystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/ability-substrate.md and
    /// docs/use-cases/ability-invocation.md.
    /// </summary>
    public sealed class AbilitySystemTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hand-rolled stub for <see cref="IAttributeSystem"/> that stores all pool values
        /// in a plain dictionary so tests can set and inspect them directly.
        /// Pool setters clamp CurrentX to [0, MaxX] for non-HP pools,
        /// and [-10, MaxX] for HP (matching the production AttributeSystem default floor).
        /// </summary>
        private sealed class StubAttributeSystem : IAttributeSystem
        {
            // Per-entity pool state
            private readonly Dictionary<uint, PoolState> _pools = new();

            private PoolState EnsurePool(uint id)
            {
                if (!_pools.TryGetValue(id, out var s))
                    _pools[id] = s = new PoolState();
                return s;
            }

            public void Seed(uint entityId, int hp, int mana, int stamina, int astra)
            {
                var p = EnsurePool(entityId);
                p.MaxHp = hp; p.CurrentHp = hp;
                p.MaxMana = mana; p.CurrentMana = mana;
                p.MaxStamina = stamina; p.CurrentStamina = stamina;
                p.MaxAstra = astra; p.CurrentAstra = astra;
            }

            // ── IAttributeSystem ─────────────────────────────────────────────────
            public int GetLevel(uint id) => 1;
            public int GetMind(uint id) => 10;
            public int GetBody(uint id) => 10;
            public int GetSpirit(uint id) => 10;
            public int GetAttunement(uint id) => 10;

            public int GetMaxHp(uint id)       => EnsurePool(id).MaxHp;
            public int GetCurrentHp(uint id)   => EnsurePool(id).CurrentHp;
            public int GetMaxMana(uint id)     => EnsurePool(id).MaxMana;
            public int GetCurrentMana(uint id) => EnsurePool(id).CurrentMana;
            public int GetMaxStamina(uint id)     => EnsurePool(id).MaxStamina;
            public int GetCurrentStamina(uint id) => EnsurePool(id).CurrentStamina;
            public int GetMaxAstra(uint id)     => EnsurePool(id).MaxAstra;
            public int GetCurrentAstra(uint id) => EnsurePool(id).CurrentAstra;

            public void SetLevel(uint id, int v)  { }
            public void SetMind(uint id, int v)   { }
            public void SetBody(uint id, int v)   { }
            public void SetSpirit(uint id, int v) { }
            public void SetAttunement(uint id, int v) { }

            public void SetMaxHp(uint id, int v) { var p = EnsurePool(id); p.MaxHp = v; }
            public void SetCurrentHp(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentHp = Math.Clamp(v, -10, p.MaxHp);
            }

            public void SetMaxMana(uint id, int v) { var p = EnsurePool(id); p.MaxMana = v; }
            public void SetCurrentMana(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentMana = Math.Clamp(v, 0, p.MaxMana);
            }

            public void SetMaxStamina(uint id, int v) { var p = EnsurePool(id); p.MaxStamina = v; }
            public void SetCurrentStamina(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentStamina = Math.Clamp(v, 0, p.MaxStamina);
            }

            public void SetMaxAstra(uint id, int v) { var p = EnsurePool(id); p.MaxAstra = v; }
            public void SetCurrentAstra(uint id, int v)
            {
                var p = EnsurePool(id);
                p.CurrentAstra = Math.Clamp(v, 0, p.MaxAstra);
            }

            private sealed class PoolState
            {
                public int MaxHp = 100; public int CurrentHp = 100;
                public int MaxMana = 50; public int CurrentMana = 50;
                public int MaxStamina = 50; public int CurrentStamina = 50;
                public int MaxAstra = 10; public int CurrentAstra = 10;
            }
        }

        /// <summary>
        /// Hand-rolled stub for <see cref="IEffectSystem"/> that records Apply calls
        /// and returns a minimal <see cref="Effect"/> so the ability pipeline sees
        /// non-null effects without requiring a real <see cref="EffectSystem"/>.
        /// </summary>
        private sealed class RecordingEffectSystem : IEffectSystem
        {
            public List<(uint Target, EffectDefinition Def, uint Source)> ApplyCalls { get; } = new();

            public Effect? Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
            {
                ApplyCalls.Add((targetEntityId, definition, sourceEntityId));

                // Return a minimal Effect so the caller can record it in AppliedEffects.
                // Use EffectLifetime.Instant for Instant kind, Timed otherwise.
                var lifetime = definition.Kind == EffectKind.Instant
                    ? EffectLifetime.Instant
                    : EffectLifetime.Timed;

                return new Effect(
                    EffectId: definition.EffectId,
                    Kind: definition.Kind,
                    Params: definition.Params,
                    Category: definition.Category,
                    Power: Math.Abs(definition.Params.BaseMagnitude),
                    Source: new EffectSource(sourceEntityId),
                    Group: null,
                    Lifetime: lifetime,
                    Duration: definition.Duration,
                    Elapsed: 0f,
                    Stacking: definition.Stacking,
                    Phase: definition.Phase);
            }

            public void Remove(uint entityId, string effectId) { }
            public void RemoveByCategory(uint entityId, EffectCategory category) { }
            public void RemoveImpermanent(uint entityId) { }
            public IReadOnlyList<Effect> GetActive(uint entityId) => Array.Empty<Effect>();
            public int GetModifiers(uint entityId, ScoreId scoreId) => 0;
            public EffectTickResult AdvanceTick(TimeSpan elapsed)
                => new EffectTickResult(Array.Empty<PeriodicApplication>(), Array.Empty<(uint, Effect)>());
        }

        // ── Factory helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds an <see cref="AbilitySystem"/> wired against the real
        /// <see cref="AbilityRegistry"/>, <see cref="EffectRegistry"/>,
        /// and <see cref="EntityStateService"/> (all in-memory), plus stubs for
        /// <see cref="IAttributeSystem"/> and <see cref="IEffectSystem"/>.
        /// Returns all components needed by individual tests.
        /// </summary>
        private static (
            AbilitySystem abilitySystem,
            EntityService ecs,
            StubAttributeSystem attributes,
            RecordingEffectSystem effects,
            IEntityStateService entityState
        ) Build()
        {
            var ecs         = new EntityService();
            var attributes  = new StubAttributeSystem();
            var effects     = new RecordingEffectSystem();
            var entityState = new EntityStateService(ecs);
            var abilityReg  = new AbilityRegistry();
            var effectReg   = new EffectRegistry();

            var system = new AbilitySystem(
                ecs,
                abilityReg,
                effects,
                effectReg,
                attributes,
                entityState);

            return (system, ecs, attributes, effects, entityState);
        }

        /// <summary>
        /// Creates a player entity with full pools and learns the named ability.
        /// </summary>
        private static uint PlayerWith(
            EntityService ecs,
            StubAttributeSystem attributes,
            AbilitySystem system,
            int hp = 100, int mana = 50, int stamina = 50, int astra = 10)
        {
            var id = new EntityBuilder(ecs).AsPlayer().Build();
            attributes.Seed(id, hp, mana, stamina, astra);
            return id;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — unknown ability
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 1: Ability not found in registry → UnknownAbility; no cost spent.
        /// </summary>
        [Fact]
        public void Activate_unknown_ability_id_returns_UnknownAbility()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            var result = system.Activate(actor, "nonexistent_ability");

            Assert.Equal(AbilityActivationOutcome.UnknownAbility, result.Outcome);
            Assert.Equal("nonexistent_ability", result.AbilityId);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — not known by entity
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 2: Known registry ability, but entity has not learned it → NotKnown.
        /// No cost should be spent.
        /// </summary>
        [Fact]
        public void Activate_ability_not_learned_returns_NotKnown()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);

            // "empower" is in the registry (Mana cost 10) but not learned by this entity.
            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.NotKnown, result.Outcome);
            // No mana spent.
            Assert.Equal(50, attributes.GetCurrentMana(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — Passive/Triggered abilities are not directly activatable
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 3: "toughness" is Passive → NotActivatable, even if the entity has learned it.
        /// </summary>
        [Fact]
        public void Activate_passive_ability_returns_NotActivatable()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);
            system.Learn(actor, "toughness");

            var result = system.Activate(actor, "toughness");

            Assert.Equal(AbilityActivationOutcome.NotActivatable, result.Outcome);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — entity state check (Incapacitated blocks)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 4: Entity is Incapacitated → StateBlocked. No cost spent.
        /// </summary>
        [Fact]
        public void Activate_when_incapacitated_returns_StateBlocked()
        {
            var (system, ecs, attributes, _, entityState) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            // Force Incapacitated state directly (bypassing the normal HP-floor path
            // since we're unit-testing the activation guard, not the state machine).
            ecs.AddComponent(actor, new EntityStateComponent { ActiveStates = EntityStateFlags.Incapacitated });

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.StateBlocked, result.Outcome);
            // No mana spent.
            Assert.Equal(50, attributes.GetCurrentMana(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — cooldown check
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 5: Ability is on cooldown → OnCooldown. No cost spent.
        /// </summary>
        [Fact]
        public void Activate_while_on_cooldown_returns_OnCooldown()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 100);
            system.Learn(actor, "empower");

            // First activation succeeds and puts empower on cooldown (30s).
            var first = system.Activate(actor, "empower");
            Assert.Equal(AbilityActivationOutcome.Activated, first.Outcome);

            var manaAfterFirst = attributes.GetCurrentMana(actor);

            // Immediately try to activate again — cooldown is active.
            var second = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.OnCooldown, second.Outcome);
            // No additional mana spent on the failed attempt.
            Assert.Equal(manaAfterFirst, attributes.GetCurrentMana(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order — insufficient resources
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 6: Entity cannot afford the cost → InsufficientResources. No cost spent.
        /// </summary>
        [Fact]
        public void Activate_with_insufficient_mana_returns_InsufficientResources()
        {
            var (system, ecs, attributes, _, _) = Build();
            // empower costs 10 Mana; seed with only 5.
            var actor = PlayerWith(ecs, attributes, system, mana: 5);
            system.Learn(actor, "empower");

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.InsufficientResources, result.Outcome);
            // No mana spent.
            Assert.Equal(5, attributes.GetCurrentMana(actor));
        }

        [Fact]
        public void Activate_with_insufficient_stamina_returns_InsufficientResources()
        {
            var (system, ecs, attributes, _, _) = Build();
            // kick costs 10 Stamina; seed with 0.
            var actor = PlayerWith(ecs, attributes, system, stamina: 0);
            system.Learn(actor, "kick");

            var result = system.Activate(actor, "kick");

            Assert.Equal(AbilityActivationOutcome.InsufficientResources, result.Outcome);
            Assert.Equal(0, attributes.GetCurrentStamina(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Atomicity: validation failure before spend block — no partial spend
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// blood_pact costs HP + Mana. If the cooldown is active (one gate fires before
        /// the spend block), neither HP nor Mana should change.
        /// This verifies the "atomic before any spend" guarantee.
        /// </summary>
        [Fact]
        public void Activate_cooldown_failure_does_not_spend_any_resource()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, hp: 100, mana: 50);
            system.Learn(actor, "blood_pact");

            // First activation: succeeds, sets cooldown, spends both HP and Mana.
            var first = system.Activate(actor, "blood_pact");
            Assert.Equal(AbilityActivationOutcome.Activated, first.Outcome);

            var hpAfterFirst    = attributes.GetCurrentHp(actor);
            var manaAfterFirst  = attributes.GetCurrentMana(actor);

            // Cooldown is now active — second attempt must fail without spending.
            var second = system.Activate(actor, "blood_pact");
            Assert.Equal(AbilityActivationOutcome.OnCooldown, second.Outcome);

            Assert.Equal(hpAfterFirst,   attributes.GetCurrentHp(actor));
            Assert.Equal(manaAfterFirst, attributes.GetCurrentMana(actor));
        }

        /// <summary>
        /// blood_pact costs HP + Mana. If HP is sufficient but Mana is insufficient,
        /// HP must NOT be spent (the cost check is atomic — all-or-nothing).
        /// </summary>
        [Fact]
        public void Activate_multi_cost_partial_failure_spends_nothing()
        {
            var (system, ecs, attributes, _, _) = Build();
            // blood_pact: Hp 10, Mana 15. Seed with enough HP but too little Mana.
            var actor = PlayerWith(ecs, attributes, system, hp: 100, mana: 5);
            system.Learn(actor, "blood_pact");

            var result = system.Activate(actor, "blood_pact");

            Assert.Equal(AbilityActivationOutcome.InsufficientResources, result.Outcome);
            // HP must be unchanged because the resource check failed before the spend block.
            Assert.Equal(100, attributes.GetCurrentHp(actor));
            Assert.Equal(5,   attributes.GetCurrentMana(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // On success: costs spent, cooldown set, effects applied
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Successful activation of "empower": Mana spent, cooldown set, effect applied.
        /// </summary>
        [Fact]
        public void Activate_success_spends_mana_cost()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            // empower costs 10 Mana.
            Assert.Equal(40, attributes.GetCurrentMana(actor));
        }

        [Fact]
        public void Activate_success_sets_cooldown()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            system.Activate(actor, "empower");

            // empower has a 30-second cooldown.
            Assert.Equal(30f, system.GetCooldownRemaining(actor, "empower"), precision: 3);
        }

        [Fact]
        public void Activate_success_applies_effects_via_effect_system()
        {
            var (system, ecs, attributes, effects, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            // The effect system should have been called for "empower".
            Assert.Contains(effects.ApplyCalls, call => call.Def.EffectId == "empower");
        }

        [Fact]
        public void Activate_success_result_contains_spent_costs()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.Single(result.Spent);
            Assert.Equal(ResourceType.Mana, result.Spent[0].Resource);
            Assert.Equal(10, result.Spent[0].Amount);
        }

        [Fact]
        public void Activate_success_result_contains_applied_effects()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.NotEmpty(result.AppliedEffects);
            Assert.Contains(result.AppliedEffects, e => e.EffectId == "empower");
        }

        /// <summary>
        /// "kick" costs Stamina. Successful activation spends exactly the declared amount.
        /// </summary>
        [Fact]
        public void Activate_kick_spends_stamina_cost()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor  = PlayerWith(ecs, attributes, system, stamina: 50);
            var target = new EntityBuilder(ecs).AsMob("goblin").Build();
            system.Learn(actor, "kick");

            var result = system.Activate(actor, "kick", target);

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            // kick costs 10 Stamina.
            Assert.Equal(40, attributes.GetCurrentStamina(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Multi-pool spend (blood_pact: HP + Mana)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// blood_pact costs HP (10) and Mana (15). On success, both pools must be reduced.
        /// </summary>
        [Fact]
        public void Activate_blood_pact_spends_both_hp_and_mana()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, hp: 100, mana: 50);
            system.Learn(actor, "blood_pact");

            var result = system.Activate(actor, "blood_pact");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.Equal(90,  attributes.GetCurrentHp(actor));   // HP cost: 10
            Assert.Equal(35,  attributes.GetCurrentMana(actor)); // Mana cost: 15
        }

        [Fact]
        public void Activate_blood_pact_result_contains_both_spent_costs()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, hp: 100, mana: 50);
            system.Learn(actor, "blood_pact");

            var result = system.Activate(actor, "blood_pact");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.Equal(2, result.Spent.Count);
            Assert.Contains(result.Spent, c => c.Resource == ResourceType.Hp   && c.Amount == 10);
            Assert.Contains(result.Spent, c => c.Resource == ResourceType.Mana && c.Amount == 15);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // resolveOffensiveExternally — skips offensive damage effect, returns OffensivePower
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// When <c>resolveOffensiveExternally = true</c>, the offensive damage effect
        /// ("kick_damage") must NOT be passed to <see cref="IEffectSystem.Apply"/>,
        /// and <see cref="AbilityActivationResult.OffensivePower"/> must be populated.
        /// </summary>
        [Fact]
        public void Activate_resolveOffensiveExternally_skips_damage_effect_and_returns_power()
        {
            var (system, ecs, attributes, effects, _) = Build();
            var actor  = PlayerWith(ecs, attributes, system, stamina: 50);
            var target = new EntityBuilder(ecs).AsMob("goblin").Build();
            system.Learn(actor, "kick");

            var result = system.Activate(actor, "kick", target, resolveOffensiveExternally: true);

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);

            // The damage effect must NOT have been applied via the effect system.
            Assert.DoesNotContain(effects.ApplyCalls, call => call.Def.EffectId == "kick_damage");

            // OffensivePower must be set (the raw BaseMagnitude of the kick_damage effect is 15).
            Assert.NotNull(result.OffensivePower);
            Assert.True(result.OffensivePower > 0, "OffensivePower must be positive");
        }

        /// <summary>
        /// When <c>resolveOffensiveExternally = false</c> (default),
        /// the damage effect IS applied normally and OffensivePower is null.
        /// </summary>
        [Fact]
        public void Activate_default_applies_offensive_effect_and_OffensivePower_is_null()
        {
            var (system, ecs, attributes, effects, _) = Build();
            var actor  = PlayerWith(ecs, attributes, system, stamina: 50);
            var target = new EntityBuilder(ecs).AsMob("goblin").Build();
            system.Learn(actor, "kick");

            var result = system.Activate(actor, "kick", target, resolveOffensiveExternally: false);

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.Contains(effects.ApplyCalls, call => call.Def.EffectId == "kick_damage");
            Assert.Null(result.OffensivePower);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // AdvanceCooldowns
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// AdvanceCooldowns decrements cooldown by elapsed time.
        /// </summary>
        [Fact]
        public void AdvanceCooldowns_decrements_cooldown_by_elapsed_time()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");
            system.Activate(actor, "empower"); // sets 30s cooldown

            system.AdvanceCooldowns(TimeSpan.FromSeconds(10));

            Assert.Equal(20f, system.GetCooldownRemaining(actor, "empower"), precision: 3);
        }

        /// <summary>
        /// AdvanceCooldowns clamps at 0 — cooldown never goes negative.
        /// </summary>
        [Fact]
        public void AdvanceCooldowns_clamps_at_zero()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");
            system.Activate(actor, "empower"); // 30s cooldown

            // Advance well past the cooldown.
            system.AdvanceCooldowns(TimeSpan.FromSeconds(100));

            // Must be 0 (entry removed) — GetCooldownRemaining returns 0 for absent key.
            Assert.Equal(0f, system.GetCooldownRemaining(actor, "empower"));
        }

        /// <summary>
        /// After cooldown expires (decremented to 0), the ability is activatable again.
        /// </summary>
        [Fact]
        public void AdvanceCooldowns_fully_expired_allows_reactivation()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 100);
            system.Learn(actor, "empower");

            // First activation.
            var first = system.Activate(actor, "empower");
            Assert.Equal(AbilityActivationOutcome.Activated, first.Outcome);

            // Fully expire cooldown.
            system.AdvanceCooldowns(TimeSpan.FromSeconds(30));

            // Second activation should succeed.
            var second = system.Activate(actor, "empower");
            Assert.Equal(AbilityActivationOutcome.Activated, second.Outcome);
        }

        /// <summary>
        /// AdvanceCooldowns advances cooldowns for all entities simultaneously.
        /// </summary>
        [Fact]
        public void AdvanceCooldowns_advances_all_entities()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor1 = PlayerWith(ecs, attributes, system, mana: 50);
            var actor2 = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor1, "empower");
            system.Learn(actor2, "empower");

            system.Activate(actor1, "empower"); // 30s cooldown on actor1
            system.Activate(actor2, "empower"); // 30s cooldown on actor2

            system.AdvanceCooldowns(TimeSpan.FromSeconds(15));

            Assert.Equal(15f, system.GetCooldownRemaining(actor1, "empower"), precision: 3);
            Assert.Equal(15f, system.GetCooldownRemaining(actor2, "empower"), precision: 3);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Learn / IsKnown / GetKnown
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Before Learn is called, IsKnown returns false.
        /// </summary>
        [Fact]
        public void IsKnown_returns_false_before_learning()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            Assert.False(system.IsKnown(actor, "empower"));
        }

        /// <summary>
        /// After Learn is called, IsKnown returns true.
        /// </summary>
        [Fact]
        public void IsKnown_returns_true_after_Learn()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            system.Learn(actor, "empower");

            Assert.True(system.IsKnown(actor, "empower"));
        }

        /// <summary>
        /// Learn returns true on the first call.
        /// </summary>
        [Fact]
        public void Learn_returns_true_on_first_call()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            var result = system.Learn(actor, "empower");

            Assert.True(result);
        }

        /// <summary>
        /// Learn is idempotent — returns false when the ability is already known.
        /// </summary>
        [Fact]
        public void Learn_is_idempotent_returns_false_if_already_known()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);
            system.Learn(actor, "empower");

            var second = system.Learn(actor, "empower");

            Assert.False(second);
            // Still known; no duplicates.
            Assert.Single(system.GetKnown(actor).Where(id => id == "empower"));
        }

        /// <summary>
        /// Learn returns false for an unknown ability id (not in the registry).
        /// </summary>
        [Fact]
        public void Learn_returns_false_for_unknown_ability_id()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            var result = system.Learn(actor, "nonexistent");

            Assert.False(result);
            Assert.False(system.IsKnown(actor, "nonexistent"));
        }

        /// <summary>
        /// GetKnown returns all abilities the entity has learned.
        /// </summary>
        [Fact]
        public void GetKnown_returns_all_learned_abilities()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            system.Learn(actor, "empower");
            system.Learn(actor, "kick");

            var known = system.GetKnown(actor);

            Assert.Contains("empower", known);
            Assert.Contains("kick", known);
        }

        /// <summary>
        /// GetKnown returns empty list for an entity that has not learned anything.
        /// </summary>
        [Fact]
        public void GetKnown_returns_empty_for_entity_with_no_abilities()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);

            Assert.Empty(system.GetKnown(actor));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Teach behaves like Learn on the student
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Teach grants the ability to the student entity; IsKnown on the student returns true.
        /// </summary>
        [Fact]
        public void Teach_grants_ability_to_student()
        {
            var (system, ecs, attributes, _, _) = Build();
            var teacher = PlayerWith(ecs, attributes, system);
            var student = PlayerWith(ecs, attributes, system);

            var result = system.Teach(teacher, student, "empower");

            Assert.True(result);
            Assert.True(system.IsKnown(student, "empower"));
            // Teacher is NOT affected.
            Assert.False(system.IsKnown(teacher, "empower"));
        }

        [Fact]
        public void Teach_is_idempotent_on_student()
        {
            var (system, ecs, attributes, _, _) = Build();
            var teacher = PlayerWith(ecs, attributes, system);
            var student = PlayerWith(ecs, attributes, system);
            system.Teach(teacher, student, "empower");

            // Second teach returns false (already known).
            var second = system.Teach(teacher, student, "empower");

            Assert.False(second);
            Assert.True(system.IsKnown(student, "empower"));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Validation order verified by sequencing: each gate fires before the next
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verify the full validation sequence: gates 1–6 all fire before any spend.
        /// The entity has insufficient mana AND an active cooldown. The cooldown gate (5)
        /// fires before the resource gate (6), so OnCooldown is the result — not InsufficientResources.
        /// No resources are spent.
        /// </summary>
        [Fact]
        public void Activate_validation_order_cooldown_before_resource_check()
        {
            var (system, ecs, attributes, _, _) = Build();
            // Mana is borderline: empower costs 10; seed 5 (not enough).
            var actor = PlayerWith(ecs, attributes, system, mana: 100);
            system.Learn(actor, "empower");

            // Activate once to set cooldown and drain mana.
            system.Activate(actor, "empower");
            // Now drain remaining mana so next attempt would also fail resource check.
            attributes.SetCurrentMana(actor, 5); // below 10 cost

            // At this point: cooldown active (30s) AND mana insufficient.
            var result = system.Activate(actor, "empower");

            // Cooldown gate (5) should fire before resource gate (6).
            Assert.Equal(AbilityActivationOutcome.OnCooldown, result.Outcome);
            // Mana is still 5 (nothing was spent).
            Assert.Equal(5, attributes.GetCurrentMana(actor));
        }

        /// <summary>
        /// With a known ability and no cooldown: when the state is Incapacitated AND
        /// the entity has insufficient mana, state gate (4) fires before resource gate (6).
        /// </summary>
        [Fact]
        public void Activate_validation_order_state_before_resource_check()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 5); // insufficient
            system.Learn(actor, "empower");
            ecs.AddComponent(actor, new EntityStateComponent { ActiveStates = EntityStateFlags.Incapacitated });

            var result = system.Activate(actor, "empower");

            Assert.Equal(AbilityActivationOutcome.StateBlocked, result.Outcome);
            Assert.Equal(5, attributes.GetCurrentMana(actor)); // no spend
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // AbilitySystem does not hold IEventBus (INV-5)
        // ═══════════════════════════════════════════════════════════════════════════

        [Fact]
        public void AbilitySystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(AbilitySystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AbilitySystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // GetCooldownRemaining
        // ═══════════════════════════════════════════════════════════════════════════

        [Fact]
        public void GetCooldownRemaining_returns_zero_before_first_activation()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system);
            system.Learn(actor, "empower");

            Assert.Equal(0f, system.GetCooldownRemaining(actor, "empower"));
        }

        [Fact]
        public void GetCooldownRemaining_returns_full_cooldown_immediately_after_activation()
        {
            var (system, ecs, attributes, _, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, mana: 50);
            system.Learn(actor, "empower");
            system.Activate(actor, "empower");

            Assert.Equal(30f, system.GetCooldownRemaining(actor, "empower"), precision: 3);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // mend: self-heal via Instant effect applies HP
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// "mend" is a self-targeted heal (Instant, HpCurrent +20).
        /// After activation the actor's HP should increase (via the attribute system stub).
        /// </summary>
        [Fact]
        public void Activate_mend_calls_effect_apply_for_mend_heal_effect()
        {
            var (system, ecs, attributes, effects, _) = Build();
            var actor = PlayerWith(ecs, attributes, system, hp: 80, mana: 50);
            system.Learn(actor, "mend");

            var result = system.Activate(actor, "mend");

            Assert.Equal(AbilityActivationOutcome.Activated, result.Outcome);
            Assert.Contains(effects.ApplyCalls, call => call.Def.EffectId == "mend_heal");
        }
    }
}
