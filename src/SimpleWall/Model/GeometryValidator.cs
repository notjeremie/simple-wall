using System.Drawing;

namespace SimpleWall.Model
{
    /// <summary>
    /// Validates saved output-window geometry against the displays actually connected
    /// right now, and supplies a sensible default when there is nothing usable to fall
    /// back on. This is the ONLY job of this class: stop a borderless, always-on-top
    /// window opening somewhere unreachable - e.g. geometry saved for a monitor that
    /// has since been unplugged, leaving nobody able to grab it.
    ///
    /// Two measured facts from the real wall PC drive every decision here, and both
    /// were wrong in the original design - do not "fix" them back:
    ///
    ///   1. The LED wall is an EXTENDED SECOND DISPLAY at X=1920, not a mirror of the
    ///      primary's top strip. \\.\DISPLAY1 (non-primary) is the wall; \\.\DISPLAY2
    ///      (primary) is the desktop. Device names/enumeration order are NOT an
    ///      ordering - Primary-ness must be decided from the <see cref="Rectangle"/>
    ///      identified as primary, explicitly passed in, never from array position or
    ///      name/index. That is why every method here takes <c>primary</c> as a
    ///      parameter instead of assuming <c>screens[0]</c>.
    ///
    ///   2. Working geometry is X=1920, Y=0, W=1964, H=256 - width 1964 DELIBERATELY
    ///      exceeds the 1920-wide panel. The source clip is 1964 wide; at W=1920 VLC
    ///      downscales it and the wall looks soft, while at W=1964 the 44px overhang is
    ///      simply cropped off-panel and the visible area is pixel-for-pixel 1:1 and
    ///      sharper. So <see cref="Validate"/> must NEVER clamp width (or height) to a
    ///      screen's bounds - a wider-than-screen window is a legitimate, deliberate
    ///      setting, and "helpfully" clamping it would silently degrade the wall.
    ///
    /// Screens are always taken as a parameter, never read from <c>Screen.AllScreens</c>
    /// internally, for the same reason the scheduler takes the clock as a parameter:
    /// it keeps this pure logic testable without a monitor attached.
    /// </summary>
    public static class GeometryValidator
    {
        /// <summary>Fallback strip size used when saved geometry has no usable width/height.</summary>
        private const int DefaultWidth = 1920;

        private const int DefaultHeight = 256;

        /// <summary>
        /// Returns <paramref name="saved"/> unchanged if it lands on (fully or partially
        /// overlaps) any connected screen - straddling the seam between two screens is a
        /// legitimate arrangement and is left alone, as is a width/height that exceeds
        /// its screen (see fact 2 above; never clamped here).
        ///
        /// If nothing connected intersects the saved location at all - e.g. a third
        /// monitor that has since been unplugged - the window is unreachable, so this
        /// snaps it onto <paramref name="primary"/> (which is always attached) at that
        /// screen's origin, preserving the saved size. A zero (or negative) width/height
        /// is repaired to a usable default strip size before anything else, since a
        /// zero-sized window is unreachable regardless of position.
        /// </summary>
        public static Rectangle Validate(Rectangle saved, Rectangle[] screens, Rectangle primary)
        {
            var size = saved.Size;
            if (size.Width <= 0) size.Width = DefaultWidth;
            if (size.Height <= 0) size.Height = DefaultHeight;

            var candidate = new Rectangle(saved.Location, size);

            foreach (var screen in screens)
                if (screen.IntersectsWith(candidate))
                    return candidate;

            return new Rectangle(primary.X, primary.Y, size.Width, size.Height);
        }

        /// <summary>
        /// The one callers should use: turns whatever is in the config into geometry to open at.
        ///
        /// A zero (or negative) saved width/height means "never configured" - no operator ever
        /// asks for a zero-sized window - so that routes to <see cref="DefaultGeometry"/>, which
        /// puts the window on the LED wall. Everything else is a real saved setting and only
        /// gets sanity-checked by <see cref="Validate"/>.
        ///
        /// This distinction is the whole point, and getting it wrong is invisible until someone
        /// is standing in front of the wall: a first run must NOT open at 0,0. The wall is an
        /// EXTENDED display at X=1920 (fact 1 above), so 0,0 is the operator's own desktop -
        /// and because 0,0 legitimately overlaps the primary screen, <see cref="Validate"/>
        /// would happily pass it through as a perfectly good setting. The window would then be
        /// a black rectangle on the wrong monitor while the wall stayed dark, which from the
        /// operator's chair is indistinguishable from "it didn't start".
        /// </summary>
        public static Rectangle Resolve(Rectangle saved, Rectangle[] screens, Rectangle primary) =>
            saved.Width <= 0 || saved.Height <= 0
                ? DefaultGeometry(screens, primary)
                : Validate(saved, screens, primary);

        /// <summary>
        /// Picks where a first-run (or otherwise absent) output geometry should default
        /// to. Prefers the first screen in <paramref name="screens"/> that is NOT
        /// <paramref name="primary"/> - on the real machine that is the LED wall at
        /// X=1920, decided purely by comparing against <paramref name="primary"/> and
        /// never by array position (see fact 1 above: DISPLAY1 is the non-primary wall,
        /// DISPLAY2 is primary - name/order carry no meaning). Falls back to
        /// <paramref name="primary"/> itself when it is the only screen present, so a
        /// single-monitor setup still gets a reachable window instead of one parked at
        /// an X=1920 that does not exist.
        ///
        /// Returns a 1920x256 strip at the chosen screen's origin. The caller resizes
        /// to taste (e.g. to the real clip's 1964 width) - this method does not attempt
        /// to guess clip dimensions.
        /// </summary>
        public static Rectangle DefaultGeometry(Rectangle[] screens, Rectangle primary)
        {
            foreach (var screen in screens)
                if (screen != primary)
                    return new Rectangle(screen.X, screen.Y, DefaultWidth, DefaultHeight);

            return new Rectangle(primary.X, primary.Y, DefaultWidth, DefaultHeight);
        }
    }
}
