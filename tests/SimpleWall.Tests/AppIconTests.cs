using System.Drawing;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    public class AppIconTests
    {
        /// <summary>
        /// A renamed resource returns null and the app quietly goes back to the stock icon,
        /// so this checks the embedded .ico is actually there under the name the code asks for.
        /// </summary>
        [Fact]
        public void TheEmbeddedIconLoads()
        {
            using (var icon = AppIcon.Load())
                Assert.NotNull(icon);
        }

        /// <summary>
        /// The title bar and taskbar draw at 16px; a missing frame means a blurry downscale. Not
        /// 256: that frame is PNG-compressed, which System.Drawing.Icon skips -- it is there for
        /// Explorer, which reads it from the exe, and no window ever draws at that size.
        /// </summary>
        [Theory]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(48)]
        public void ItCarriesARealFrameAt(int size)
        {
            using (var icon = AppIcon.Load())
            using (var sized = new Icon(icon, size, size))
                Assert.Equal(size, sized.Width);
        }
    }
}
