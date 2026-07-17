using System.Windows.Forms;

namespace SimpleWall.UI
{
    /// <summary>
    /// A TrackBar that ignores the mouse wheel.
    ///
    /// WM_MOUSEWHEEL goes to the FOCUSED control, not the one under the pointer (Win7 has no
    /// scroll-inactive-windows). So once the operator has touched a slider it keeps focus, and
    /// every later wheel spin anywhere in the window -- notably trying to scroll the clip grid to
    /// reach slot 30 -- would move brightness on the wall, 0.03 per notch, live, and the drift
    /// would then silently revert on the next restart. "The wall's brightness keeps changing on
    /// its own and won't stay put" is a miserable thing to diagnose from another country.
    ///
    /// The wheel buys nothing here that dragging or the arrow keys don't.
    /// </summary>
    public class WallTrackBar : TrackBar
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Deliberately does not call base -- that is the entire point of this class.
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }
    }
}
