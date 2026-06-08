using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Prompt.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Xunit;

namespace Hedron.Tests.Output
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="PromptComposerSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/prompt-and-output-batching.md.
    ///   - Returns null when playerEntityId is 0 (no bound entity).
    ///   - Returns a PromptMessage (non-null) for any non-zero entity id.
    ///   - StateLabel is null when no abnormal flags are set.
    ///   - StateLabel is "(Fighting)" when InCombat is set.
    ///   - StateLabel is "(Resting)" when Resting is set.
    ///   - StateLabel is "(Incapacitated)" when Incapacitated is set.
    ///   - Incapacitated wins over InCombat (highest-priority label).
    ///   - Pools with max == 0 are omitted from the message.
    ///   - Pools with max > 0 appear in the Pools collection.
    ///   - HP/Mana/Stamina/Astra pools all appear when all maxes are non-zero.
    ///   - Current and Max values on each PoolDisplay match what IStatSystem returns.
    ///   - INV-5: PromptComposerSystem holds no IEventBus field.
    /// </summary>
    public sealed class PromptComposerSystemTests
    {
        // ── Hand-rolled stubs ────────────────────────────────────────────────────

        /// <summary>
        /// Stub <see cref="IEntityStateService"/> that returns a fixed set of flags per entity.
        /// </summary>
        private sealed class StubEntityStateService : IEntityStateService
        {
            private readonly Dictionary<uint, EntityStateFlags> _states = new();

            public void SetState(uint entityId, EntityStateFlags flags) =>
                _states[entityId] = flags;

            public EntityStateFlags GetStates(uint entityId) =>
                _states.TryGetValue(entityId, out var f) ? f : EntityStateFlags.None;

            public bool IsInState(uint entityId, EntityStateFlags state) =>
                (GetStates(entityId) & state) != 0;

            public bool TryEnterState(uint entityId, EntityStateFlags state, out string? failReason)
            {
                SetState(entityId, GetStates(entityId) | state);
                failReason = null;
                return true;
            }

            public void ExitState(uint entityId, EntityStateFlags state) =>
                _states[entityId] = GetStates(entityId) & ~state;
        }

        /// <summary>
        /// Stub <see cref="IStatSystem"/> that returns fixed values keyed by (entityId, ScoreId).
        /// All unregistered scores return 0 (so pools with max=0 are omitted).
        /// </summary>
        private sealed class StubStatSystem : IStatSystem
        {
            private readonly Dictionary<(uint, ScoreId), int> _values = new();

            public void Set(uint entityId, ScoreId score, int value) =>
                _values[(entityId, score)] = value;

            public int Get(uint entityId, ScoreId score) =>
                _values.TryGetValue((entityId, score), out var v) ? v : 0;

            // ── Typed getters delegate to Get ────────────────────────────────────
            public int GetEffectiveMind(uint entityId)        => Get(entityId, ScoreId.Mind);
            public int GetEffectiveBody(uint entityId)        => Get(entityId, ScoreId.Body);
            public int GetEffectiveSpirit(uint entityId)      => Get(entityId, ScoreId.Spirit);
            public int GetEffectiveAttunement(uint entityId)  => Get(entityId, ScoreId.Attunement);
            public int GetEffectiveAttackPower(uint entityId) => Get(entityId, ScoreId.AttackPower);
            public int GetEffectiveDefense(uint entityId)     => Get(entityId, ScoreId.Defense);
            public int GetCurrentHp(uint entityId)            => Get(entityId, ScoreId.HpCurrent);
            public int GetMaxHp(uint entityId)                => Get(entityId, ScoreId.HpMax);
        }

        // ── Factory helper ───────────────────────────────────────────────────────

        private static (PromptComposerSystem sut, StubEntityStateService states, StubStatSystem stats)
            Build()
        {
            var states = new StubEntityStateService();
            var stats = new StubStatSystem();
            var sut = new PromptComposerSystem(states, stats);
            return (sut, states, stats);
        }

        /// <summary>
        /// Registers all four pool maxes and currents on the stub stat system for the given entity.
        /// </summary>
        private static void SetPools(StubStatSystem stats, uint entityId,
            int hp = 100, int mana = 50, int stamina = 50, int astra = 10,
            int? currentHp = null, int? currentMana = null,
            int? currentStamina = null, int? currentAstra = null)
        {
            stats.Set(entityId, ScoreId.HpMax, hp);
            stats.Set(entityId, ScoreId.HpCurrent, currentHp ?? hp);
            stats.Set(entityId, ScoreId.ManaMax, mana);
            stats.Set(entityId, ScoreId.ManaCurrent, currentMana ?? mana);
            stats.Set(entityId, ScoreId.StaminaMax, stamina);
            stats.Set(entityId, ScoreId.StaminaCurrent, currentStamina ?? stamina);
            stats.Set(entityId, ScoreId.AstraMax, astra);
            stats.Set(entityId, ScoreId.AstraCurrent, currentAstra ?? astra);
        }

        // ── Null / zero-id guard ─────────────────────────────────────────────────

        [Fact]
        public void GetPrompt_returns_null_for_entity_id_zero()
        {
            var (sut, _, _) = Build();

            var result = sut.GetPrompt(0u);

            Assert.Null(result);
        }

        // ── Returns PromptMessage for non-zero entity ────────────────────────────

        [Fact]
        public void GetPrompt_returns_PromptMessage_for_non_zero_entity()
        {
            var (sut, _, stats) = Build();
            SetPools(stats, entityId: 1u);

            var result = sut.GetPrompt(1u);

            Assert.NotNull(result);
            Assert.IsType<PromptMessage>(result);
        }

        // ── StateLabel: normal state (no flags) ──────────────────────────────────

        [Fact]
        public void GetPrompt_StateLabel_is_null_when_no_flags_set()
        {
            var (sut, states, stats) = Build();
            SetPools(stats, entityId: 1u);
            states.SetState(1u, EntityStateFlags.None);

            var result = sut.GetPrompt(1u);

            Assert.NotNull(result);
            Assert.Null(result!.StateLabel);
        }

        [Fact]
        public void GetPrompt_StateLabel_is_null_for_entity_with_no_state_component()
        {
            // No state registered at all — defaults to None.
            var (sut, _, stats) = Build();
            SetPools(stats, entityId: 5u);

            var result = sut.GetPrompt(5u);

            Assert.Null(result!.StateLabel);
        }

        // ── StateLabel: InCombat → (Fighting) ────────────────────────────────────

        [Fact]
        public void GetPrompt_StateLabel_is_Fighting_when_InCombat()
        {
            var (sut, states, stats) = Build();
            SetPools(stats, entityId: 2u);
            states.SetState(2u, EntityStateFlags.InCombat);

            var result = sut.GetPrompt(2u);

            Assert.NotNull(result);
            Assert.Equal("(Fighting)", result!.StateLabel);
        }

        // ── StateLabel: Resting → (Resting) ──────────────────────────────────────

        [Fact]
        public void GetPrompt_StateLabel_is_Resting_when_Resting()
        {
            var (sut, states, stats) = Build();
            SetPools(stats, entityId: 3u);
            states.SetState(3u, EntityStateFlags.Resting);

            var result = sut.GetPrompt(3u);

            Assert.NotNull(result);
            Assert.Equal("(Resting)", result!.StateLabel);
        }

        // ── StateLabel: Incapacitated → (Incapacitated) ──────────────────────────

        [Fact]
        public void GetPrompt_StateLabel_is_Incapacitated_when_Incapacitated()
        {
            var (sut, states, stats) = Build();
            SetPools(stats, entityId: 4u);
            states.SetState(4u, EntityStateFlags.Incapacitated);

            var result = sut.GetPrompt(4u);

            Assert.NotNull(result);
            Assert.Equal("(Incapacitated)", result!.StateLabel);
        }

        // ── StateLabel: priority — Incapacitated wins over InCombat ─────────────

        [Fact]
        public void GetPrompt_Incapacitated_wins_over_InCombat_for_StateLabel()
        {
            var (sut, states, stats) = Build();
            SetPools(stats, entityId: 6u);
            // Both flags set — Incapacitated has highest priority.
            states.SetState(6u, EntityStateFlags.InCombat | EntityStateFlags.Incapacitated);

            var result = sut.GetPrompt(6u);

            Assert.NotNull(result);
            Assert.Equal("(Incapacitated)", result!.StateLabel);
        }

        // ── Pools: omitted when max == 0 ─────────────────────────────────────────

        [Fact]
        public void GetPrompt_omits_pool_when_max_is_zero()
        {
            var (sut, _, stats) = Build();
            const uint id = 7u;
            // Only HP has non-zero max; Mana/Stamina/Astra max = 0 (default).
            stats.Set(id, ScoreId.HpMax, 100);
            stats.Set(id, ScoreId.HpCurrent, 80);
            // Mana, Stamina, Astra are not set → GetMax returns 0 → omitted.

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            Assert.Single(result!.Pools);
            Assert.Equal("HP", result.Pools[0].Name);
        }

        [Fact]
        public void GetPrompt_returns_empty_pools_when_all_maxes_are_zero()
        {
            var (sut, _, _) = Build();
            // No scores registered → all maxes are 0 → no pools.

            var result = sut.GetPrompt(8u);

            Assert.NotNull(result);
            Assert.Empty(result!.Pools);
        }

        // ── Pools: all four present when all maxes > 0 ───────────────────────────

        [Fact]
        public void GetPrompt_includes_all_four_pools_when_all_maxes_are_nonzero()
        {
            var (sut, _, stats) = Build();
            const uint id = 9u;
            SetPools(stats, id, hp: 100, mana: 50, stamina: 60, astra: 20);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            Assert.Equal(4, result!.Pools.Count);

            var names = result.Pools.Select(p => p.Name).ToList();
            Assert.Contains("HP", names);
            Assert.Contains("Mana", names);
            Assert.Contains("Stamina", names);
            Assert.Contains("Astra", names);
        }

        // ── Pools: current/max values match IStatSystem output ──────────────────

        [Fact]
        public void GetPrompt_HP_pool_reflects_current_and_max_from_StatSystem()
        {
            var (sut, _, stats) = Build();
            const uint id = 10u;
            stats.Set(id, ScoreId.HpMax, 120);
            stats.Set(id, ScoreId.HpCurrent, 45);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            var hp = result!.Pools.Single(p => p.Name == "HP");
            Assert.Equal(45, hp.Current);
            Assert.Equal(120, hp.Max);
        }

        [Fact]
        public void GetPrompt_Mana_pool_reflects_current_and_max_from_StatSystem()
        {
            var (sut, _, stats) = Build();
            const uint id = 11u;
            stats.Set(id, ScoreId.ManaMax, 75);
            stats.Set(id, ScoreId.ManaCurrent, 30);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            var mana = result!.Pools.Single(p => p.Name == "Mana");
            Assert.Equal(30, mana.Current);
            Assert.Equal(75, mana.Max);
        }

        [Fact]
        public void GetPrompt_Stamina_pool_reflects_current_and_max_from_StatSystem()
        {
            var (sut, _, stats) = Build();
            const uint id = 12u;
            stats.Set(id, ScoreId.StaminaMax, 60);
            stats.Set(id, ScoreId.StaminaCurrent, 15);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            var stamina = result!.Pools.Single(p => p.Name == "Stamina");
            Assert.Equal(15, stamina.Current);
            Assert.Equal(60, stamina.Max);
        }

        [Fact]
        public void GetPrompt_Astra_pool_reflects_current_and_max_from_StatSystem()
        {
            var (sut, _, stats) = Build();
            const uint id = 13u;
            stats.Set(id, ScoreId.AstraMax, 25);
            stats.Set(id, ScoreId.AstraCurrent, 10);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            var astra = result!.Pools.Single(p => p.Name == "Astra");
            Assert.Equal(10, astra.Current);
            Assert.Equal(25, astra.Max);
        }

        // ── State + pools together ───────────────────────────────────────────────

        [Fact]
        public void GetPrompt_combat_state_and_pools_are_both_reflected()
        {
            // A player in combat with non-zero pools gets a (Fighting) label and pool data.
            var (sut, states, stats) = Build();
            const uint id = 14u;
            SetPools(stats, id, hp: 100, mana: 50, stamina: 50, astra: 10,
                currentHp: 60, currentMana: 40, currentStamina: 50, currentAstra: 10);
            states.SetState(id, EntityStateFlags.InCombat);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            Assert.Equal("(Fighting)", result!.StateLabel);
            Assert.Equal(4, result.Pools.Count);
            var hp = result.Pools.Single(p => p.Name == "HP");
            Assert.Equal(60, hp.Current);
        }

        // ── Structural difference: combat vs normal prompt ───────────────────────

        [Fact]
        public void GetPrompt_combat_prompt_has_non_null_StateLabel_normal_does_not()
        {
            // The two prompts for the same entity in different states are structurally different:
            // the combat prompt carries a non-null label; the normal prompt does not.
            var (sut, states, stats) = Build();
            const uint id = 15u;
            SetPools(stats, id);

            // Normal state.
            var normalPrompt = sut.GetPrompt(id);
            Assert.Null(normalPrompt!.StateLabel);

            // Enter combat.
            states.SetState(id, EntityStateFlags.InCombat);
            var combatPrompt = sut.GetPrompt(id);
            Assert.NotNull(combatPrompt!.StateLabel);

            // The two prompts differ structurally.
            Assert.NotEqual(normalPrompt.StateLabel, combatPrompt.StateLabel);
        }

        // ── PromptMessage category is System ────────────────────────────────────

        [Fact]
        public void GetPrompt_message_category_is_System()
        {
            var (sut, _, stats) = Build();
            const uint id = 16u;
            SetPools(stats, id);

            var result = sut.GetPrompt(id);

            Assert.NotNull(result);
            Assert.Equal(OutputCategory.System, result!.Category);
        }

        // ── INV-5: PromptComposerSystem holds no IEventBus field ────────────────

        [Fact]
        public void PromptComposerSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(PromptComposerSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: PromptComposerSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus.");
            }
        }
    }
}
