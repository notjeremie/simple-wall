using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Cover-fit crops the source to the OUTPUT window's aspect ratio so a wrong-sized clip fills
    /// the wall instead of letterboxing. The ratio string handed to libvlc must be reduced and
    /// well-formed; a bogus one is silently ignored and the black bars come back.
    /// </summary>
    public class CropRatioTests
    {
        [Theory]
        [InlineData(1664, 256, "13:2")]      // the real wall
        [InlineData(1920, 1080, "16:9")]
        [InlineData(1000, 1000, "1:1")]
        [InlineData(1963, 256, "1963:256")]  // coprime -> left as-is
        public void ReducesToLowestTerms(int width, int height, string expected)
        {
            Assert.Equal(expected, VlcWallEngine.CropRatio(width, height));
        }

        [Theory]
        [InlineData(0, 256)]
        [InlineData(1664, 0)]
        [InlineData(-5, 256)]
        public void ZeroOrNegativeGeometryIsNullNotABogusCrop(int width, int height)
        {
            // A never-resolved (zero) geometry must not produce a "0:256" crop string that libvlc
            // would choke on -- the caller skips the crop entirely when this is null.
            Assert.Null(VlcWallEngine.CropRatio(width, height));
        }
    }
}
