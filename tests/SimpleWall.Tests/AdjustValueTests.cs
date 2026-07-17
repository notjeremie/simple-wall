using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    public class AdjustValueTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(1f, 1f)]
        [InlineData(2f, 2f)]
        [InlineData(0.5f, 0.5f)]
        public void ValuesInRangePassThroughUntouched(float input, float expected)
        {
            Assert.Equal(expected, AdjustValue.Clamp(input));
        }

        [Theory]
        [InlineData(50f, 2f)]
        [InlineData(-5f, 0f)]
        [InlineData(float.PositiveInfinity, 2f)]
        [InlineData(float.NegativeInfinity, 0f)]
        public void OutOfRangeClampsToTheEnds(float input, float expected)
        {
            Assert.Equal(expected, AdjustValue.Clamp(input));
        }

        /// <summary>
        /// The trap this class exists for. Math.Min/Max PROPAGATE NaN rather than clamping it, so
        /// the obvious Math.Max(0, Math.Min(2, v)) is not a clamp at all for this one input. It
        /// has now got past review three times in this repo.
        /// </summary>
        [Fact]
        public void NaNBecomesNeutralRatherThanEscaping()
        {
            var clamped = AdjustValue.Clamp(float.NaN);

            Assert.False(float.IsNaN(clamped), "NaN escaped the clamp -- it would reach a native VLC call");
            Assert.Equal(AdjustValue.Neutral, clamped);
        }

        /// <summary>
        /// 1e40 is an ordinary-looking decimal, not an exotic literal. It overflows float to
        /// infinity on parse, and config.json is deliberately not range-validated on load.
        /// </summary>
        [Fact]
        public void AnOverflowingConfigValueIsStillBounded()
        {
            var clamped = AdjustValue.Clamp((float)1e40);

            Assert.Equal(AdjustValue.Max, clamped);
        }

        [Fact]
        public void NeutralIsNotBlack()
        {
            // A wall that defaults to 0 brightness is a wall that looks broken.
            Assert.Equal(1f, AdjustValue.Neutral);
            Assert.True(AdjustValue.Neutral > AdjustValue.Min);
        }
    }
}
