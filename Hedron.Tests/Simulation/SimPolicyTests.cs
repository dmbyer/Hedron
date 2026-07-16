using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 1 — the three built-in <see cref="ISimCombatantPolicy"/> decision tables (Postcondition 7).</summary>
    public sealed class SimPolicyTests
    {
        private static SandboxWorld NewWorld()
        {
            var factory = new SandboxWorldFactory(
                new AbilityRegistry(), new EffectRegistry(),
                new PowerBudgetSystem(PowerBudgetTunables.Default), Options.Create(new DeathOptions()));
            return factory.Create(new FakeRandom(1));
        }

        private static uint NewCombatant(SandboxWorld world, int stamina = 50, int mana = 50)
        {
            var entity = world.EntityService.CreateEntity();
            world.EntityService.AddComponent(entity.Id, new AttributesComponent { Body = 10, Mind = 10, Spirit = 10, Attunement = 10, Level = 1 });
            world.EntityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = 100, CurrentHp = 100,
                MaxMana = 50, CurrentMana = mana,
                MaxStamina = 50, CurrentStamina = stamina,
                MaxAstra = 10, CurrentAstra = 10,
            });
            return entity.Id;
        }

        // ── MeleeOnlyPolicy ───────────────────────────────────────────────────

        [Fact]
        public void MeleeOnlyPolicy_AlwaysReturnsMelee()
        {
            var world = NewWorld();
            var self = NewCombatant(world);
            world.Abilities.Learn(self, "kick");

            var policy = new MeleeOnlyPolicy();
            Assert.IsType<SimAction.MeleeAttack>(policy.ChooseAction(world, self, self, 0));
        }

        // ── RoundRobinPolicy ──────────────────────────────────────────────────

        [Fact]
        public void RoundRobinPolicy_EmptyKit_ReturnsMelee()
        {
            var world = NewWorld();
            var self = NewCombatant(world);

            var policy = new RoundRobinPolicy();
            Assert.IsType<SimAction.MeleeAttack>(policy.ChooseAction(world, self, self, 0));
        }

        [Fact]
        public void RoundRobinPolicy_CyclesThroughKnownAbilities()
        {
            var world = NewWorld();
            var self = NewCombatant(world);
            world.Abilities.Learn(self, "kick");
            world.Abilities.Learn(self, "empower");

            var policy = new RoundRobinPolicy();
            var first = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 0));
            var second = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 1));
            var third = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 2));

            Assert.Equal("kick", first.AbilityId);
            Assert.Equal("empower", second.AbilityId);
            Assert.Equal("kick", third.AbilityId); // wraps
        }

        // ── CooldownFirstPolicy ───────────────────────────────────────────────

        [Fact]
        public void CooldownFirstPolicy_EmptyKit_ReturnsMelee()
        {
            var world = NewWorld();
            var self = NewCombatant(world);

            var policy = new CooldownFirstPolicy(new AbilityRegistry());
            Assert.IsType<SimAction.MeleeAttack>(policy.ChooseAction(world, self, self, 0));
        }

        [Fact]
        public void CooldownFirstPolicy_PicksFirstOffCooldownAffordableAbility()
        {
            var world = NewWorld();
            var self = NewCombatant(world);
            world.Abilities.Learn(self, "kick");
            world.Abilities.Learn(self, "empower");

            var policy = new CooldownFirstPolicy(new AbilityRegistry());
            var action = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 0));
            Assert.Equal("kick", action.AbilityId);
        }

        [Fact]
        public void CooldownFirstPolicy_SkipsAbilityOnCooldown()
        {
            var world = NewWorld();
            var self = NewCombatant(world);
            world.Abilities.Learn(self, "kick");
            world.Abilities.Learn(self, "empower");

            // Put "kick" on cooldown via the real system entry point (mirrors live behavior).
            world.Abilities.Activate(self, "kick", self);

            var policy = new CooldownFirstPolicy(new AbilityRegistry());
            var action = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 0));
            Assert.Equal("empower", action.AbilityId);
        }

        [Fact]
        public void CooldownFirstPolicy_SkipsUnaffordableAbility()
        {
            var world = NewWorld();
            var self = NewCombatant(world, stamina: 0, mana: 50);
            world.Abilities.Learn(self, "kick");    // costs 10 stamina — unaffordable
            world.Abilities.Learn(self, "empower"); // costs 10 mana — affordable

            var policy = new CooldownFirstPolicy(new AbilityRegistry());
            var action = Assert.IsType<SimAction.UseAbility>(policy.ChooseAction(world, self, self, 0));
            Assert.Equal("empower", action.AbilityId);
        }

        [Fact]
        public void CooldownFirstPolicy_AllUnusable_ReturnsMelee()
        {
            var world = NewWorld();
            var self = NewCombatant(world, stamina: 0, mana: 0);
            world.Abilities.Learn(self, "kick");
            world.Abilities.Learn(self, "empower");

            var policy = new CooldownFirstPolicy(new AbilityRegistry());
            Assert.IsType<SimAction.MeleeAttack>(policy.ChooseAction(world, self, self, 0));
        }
    }
}
