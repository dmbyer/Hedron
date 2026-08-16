using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="MobPowerReadoutSystem"/>, the single composition of
    /// projection + oracle + drift tolerance that the Blazor <c>MobEditor</c> and the authoring API's
    /// power endpoint both render (authoring-api-surface WP2, INV-19).
    /// </summary>
    public sealed class MobPowerReadoutSystemTests
    {
        private static readonly IBalanceStandardsRegistry Standards =
            new BalanceStandardsRegistry(BalanceStandardsDefaults.Document);

        private static MobPowerReadoutSystem NewSystem() => new(
            new PowerBudgetSystem(Standards.Tunables),
            new MobPowerProjectionSystem(),
            Standards);

        private static MobTemplate Mob(int tier = 0, int band = 0, int body = 10, int maxHp = 100) =>
            new("mob.readout")
            {
                Name = "Readout",
                Tier = tier,
                Band = band,
                Body = body,
                MaxHp = maxHp,
                Mind = 10,
                Spirit = 10,
                Attunement = 10,
            };

        [Fact]
        public void Power_matches_the_oracle_over_the_shared_projection_seam()
        {
            var template = Mob(tier: 2, band: 1);

            var readout = NewSystem().Read(template);

            var expected = new PowerBudgetSystem(Standards.Tunables)
                .Estimate(new MobPowerProjectionSystem().Project(template), template.Tier);
            Assert.Equal(expected, readout.Power);
        }

        [Fact]
        public void Computed_cell_matches_Classify_of_the_estimated_power()
        {
            var readout = NewSystem().Read(Mob(tier: 3, band: 2, body: 40, maxHp: 400));

            Assert.Equal(
                new PowerBudgetSystem(Standards.Tunables).Classify(readout.Power),
                readout.Computed);
        }

        [Fact]
        public void The_authored_tier_and_band_are_echoed_unchanged()
        {
            var readout = NewSystem().Read(Mob(tier: 4, band: 3));

            Assert.Equal(4, readout.AuthoredTier);
            Assert.Equal(3, readout.AuthoredBand);
        }

        [Fact]
        public void An_authored_cell_carries_its_target_range()
        {
            var readout = NewSystem().Read(Mob(tier: 2, band: 2));

            Assert.Equal(
                new PowerBudgetSystem(Standards.Tunables).TargetRange(2, 2),
                readout.AuthoredTargetRange);
        }

        [Fact]
        public void An_unbanded_mob_has_no_target_range()
        {
            Assert.Null(NewSystem().Read(Mob(tier: 2, band: 0)).AuthoredTargetRange);
        }

        [Fact]
        public void An_out_of_table_authored_cell_yields_no_target_range_rather_than_throwing()
        {
            // Tier is authored freely on the template; the oracle fails fast on an out-of-table
            // cell, and a readout is a display, not a place to surface that as an exception.
            var readout = NewSystem().Read(Mob(tier: Standards.Tunables.MaxTier + 5, band: 1));

            Assert.Null(readout.AuthoredTargetRange);
        }

        // ── Drift ─────────────────────────────────────────────────────────────────

        [Fact]
        public void An_unbanded_mob_never_drifts()
        {
            // Authored band 0 is "unbanded", not "band zero" — it makes no claim to compare against
            // (the same exclusion IBalanceAuditSystem's sweep applies). Deliberately far off-cell.
            var readout = NewSystem().Read(Mob(tier: 0, band: 0, body: 900, maxHp: 9000));

            Assert.NotEqual(0, readout.Computed.Tier);
            Assert.False(readout.DriftsFromAuthoredCell);
        }

        [Fact]
        public void A_mob_authored_at_its_computed_cell_does_not_drift()
        {
            // The reference base build classifies to (0, 1) — pinned by PowerBudgetSystemTests.
            var readout = NewSystem().Read(Mob(tier: 0, band: 1));

            Assert.Equal(0, readout.Computed.Tier);
            Assert.Equal(1, readout.Computed.Band);
            Assert.False(readout.DriftsFromAuthoredCell);
        }

        [Fact]
        public void A_wildly_overbuilt_mob_authored_at_the_bottom_cell_drifts()
        {
            var readout = NewSystem().Read(Mob(tier: 0, band: 1, body: 900, maxHp: 9000));

            Assert.True(readout.DriftsFromAuthoredCell);
        }

        [Fact]
        public void The_readout_writes_nothing_to_the_template()
        {
            var template = Mob(tier: 2, band: 2, body: 33, maxHp: 333);

            NewSystem().Read(template);

            Assert.Equal(2, template.Tier);
            Assert.Equal(2, template.Band);
            Assert.Equal(33, template.Body);
            Assert.Equal(333, template.MaxHp);
        }
    }
}
