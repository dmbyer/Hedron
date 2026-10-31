using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.World;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="AreaDesignation"/> (containment, the admin grammar,
    /// display text) and its single shape check <see cref="AreaDesignationValidation"/> (R1, R2).
    /// </summary>
    public sealed class AreaDesignationTests
    {
        // ── Contains ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0, 1, true)]
        [InlineData(0, 2, true)]
        [InlineData(0, 3, false)]  // above
        [InlineData(1, 1, false)]  // other tier
        public void Contains_band_range_within_one_tier(int tier, int band, bool expected)
        {
            var designation = AreaDesignation.Of(0, 1, 2);
            Assert.Equal(expected, designation.Contains(new PowerBand(tier, band)));
        }

        [Fact]
        public void Contains_single_band_excludes_neighbours()
        {
            var designation = AreaDesignation.Of(0, 2, 2);
            Assert.False(designation.Contains(new PowerBand(0, 1))); // below
            Assert.True(designation.Contains(new PowerBand(0, 2)));
            Assert.False(designation.Contains(new PowerBand(0, 3)));
        }

        // ── TryParse ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("0 2", 0, 2, 2)]
        [InlineData("0 1-2", 0, 1, 2)]
        [InlineData("  1   3 ", 1, 3, 3)]
        public void TryParse_accepts_single_band_and_range(string text, int tier, int min, int max)
        {
            Assert.True(AreaDesignation.TryParse(text, out var designation, out var error));
            Assert.Null(error);
            Assert.Equal(AreaDesignation.Of(tier, min, max), designation);
        }

        [Theory]
        [InlineData("x 1")]
        [InlineData("0")]
        [InlineData("0 1-")]
        [InlineData("0 a-b")]
        [InlineData("0 1-2-3")]
        [InlineData("")]
        public void TryParse_rejects_malformed_text_with_a_reason(string text)
        {
            Assert.False(AreaDesignation.TryParse(text, out var designation, out var error));
            Assert.Null(designation);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("clear")]
        [InlineData(" NONE ")]
        public void TryParseOrClear_reads_clear_and_none_as_no_designation(string text)
        {
            Assert.True(AreaDesignation.TryParseOrClear(text, out var designation, out var error));
            Assert.Null(designation);
            Assert.Null(error);
        }

        [Fact]
        public void TryParseOrClear_delegates_everything_else_to_TryParse()
        {
            Assert.True(AreaDesignation.TryParseOrClear("0 1-2", out var designation, out _));
            Assert.Equal(AreaDesignation.Of(0, 1, 2), designation);
            Assert.False(AreaDesignation.TryParseOrClear("bogus", out _, out var error));
            Assert.NotNull(error);
        }

        // ── ToString ─────────────────────────────────────────────────────────────

        [Fact]
        public void ToString_distinguishes_single_band_from_range()
        {
            Assert.Equal("Tier 0, Band 2", AreaDesignation.Of(0, 2, 2).ToString());
            Assert.Equal("Tier 0, Bands 1-2", AreaDesignation.Of(0, 1, 2).ToString());
        }

        // ── AreaDesignationValidation ────────────────────────────────────────────

        [Fact]
        public void Valid_designation_has_no_errors()
        {
            Assert.Empty(AreaDesignationValidation.Errors("area.a", AreaDesignation.Of(0, 1, 3)));
            Assert.Empty(AreaDesignationValidation.Errors("area.a", AreaDesignation.Of(AscensionConstants.MaxTier, 2, 2)));
        }

        [Fact]
        public void Each_shape_violation_is_an_error_naming_the_area()
        {
            var cases = new[]
            {
                AreaDesignation.Of(-1, 1, 1),
                AreaDesignation.Of(AscensionConstants.MaxTier + 1, 1, 1),
                AreaDesignation.Of(0, 0, 1),
                AreaDesignation.Of(0, 1, 4),
                AreaDesignation.Of(0, 3, 1),
                new AreaDesignation(new PowerBand(0, 3), new PowerBand(1, 1)), // cross-tier
            };

            foreach (var designation in cases)
            {
                var errors = AreaDesignationValidation.Errors("area.a", designation);
                Assert.NotEmpty(errors);
                Assert.All(errors, e => Assert.Contains("area.a", e));
            }
        }
    }
}
