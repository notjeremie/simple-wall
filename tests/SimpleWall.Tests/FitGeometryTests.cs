using SimpleWall.Engine;
using SimpleWall.Model;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The crop-vs-stretch decision, pure so it is tested without libvlc. Both properties are
    /// always returned -- one gets the ratio, the OTHER gets null -- because the A/B players are
    /// reused across clips and a stale crop or aspect must be cleared, never left behind.
    /// </summary>
    public class FitGeometryTests
    {
        [Fact]
        public void CropSetsCropGeometryAndClearsAspect()
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(FitMode.Crop, 1664, 256);
            Assert.Equal("13:2", crop);
            Assert.Null(aspect);
        }

        [Fact]
        public void StretchSetsAspectAndClearsCrop()
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(FitMode.Stretch, 1664, 256);
            Assert.Null(crop);
            Assert.Equal("13:2", aspect);
        }

        [Theory]
        [InlineData(FitMode.Crop)]
        [InlineData(FitMode.Stretch)]
        public void ZeroGeometryIsNullNullSoNothingBogusIsPushed(FitMode mode)
        {
            var (crop, aspect) = VlcWallEngine.FitGeometry(mode, 0, 256);
            Assert.Null(crop);
            Assert.Null(aspect);
        }

        [Theory]
        [InlineData(0f, FitMode.Crop)]
        [InlineData(1f, FitMode.Stretch)]
        [InlineData(0.4f, FitMode.Crop)]     // defensive: rounds to nearest valid mode
        [InlineData(0.6f, FitMode.Stretch)]
        public void FitFromValueDecodesTheMode(float value, FitMode expected)
        {
            Assert.Equal(expected, VlcWallEngine.FitFromValue(value));
        }
    }
}
