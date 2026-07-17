using System.Reflection;
using System.Windows.Forms;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    public class WallTrackBarTests
    {
        private static void SendWheelNotch(TrackBar bar)
        {
            var onMouseWheel = typeof(TrackBar).GetMethod("OnMouseWheel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            onMouseWheel.Invoke(bar, new object[]
            {
                new HandledMouseEventArgs(MouseButtons.None, 0, 10, 10, SystemInformation.MouseWheelScrollDelta)
            });
        }

        /// <summary>
        /// The control test. If this ever fails, TrackBar stopped handling the wheel in
        /// OnMouseWheel and WallTrackBar's override is no longer suppressing anything -- which
        /// would make the test below pass for entirely the wrong reason.
        /// </summary>
        [Fact]
        public void APlainTrackBarDoesMoveOnTheWheel()
        {
            using (var bar = new TrackBar { Minimum = 0, Maximum = 200, Value = 100 })
            {
                SendWheelNotch(bar);

                Assert.NotEqual(100, bar.Value);
            }
        }

        /// <summary>
        /// The wheel goes to the FOCUSED control, so a slider that accepts it changes wall
        /// brightness while the operator thinks they're scrolling the clip grid.
        /// </summary>
        [Fact]
        public void OursDoesNot()
        {
            using (var bar = new WallTrackBar { Minimum = 0, Maximum = 200, Value = 100 })
            {
                SendWheelNotch(bar);

                Assert.Equal(100, bar.Value);
            }
        }
    }
}
