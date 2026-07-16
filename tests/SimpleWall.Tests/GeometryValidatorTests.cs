using System.Drawing;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class GeometryValidatorTests
    {
        // Mirrors the real wall PC: primary desktop at 0,0; LED wall extended at X=1920.
        private static readonly Rectangle Primary = new Rectangle(0, 0, 1920, 1080);
        private static readonly Rectangle LedWall = new Rectangle(1920, 0, 1920, 1080);
        private static readonly Rectangle[] TwoScreens = { Primary, LedWall };

        [Fact]
        public void GeometryOnAConnectedScreenIsKept()
        {
            var saved = new Rectangle(1920, 0, 1920, 256);
            Assert.Equal(saved, GeometryValidator.Validate(saved, TwoScreens, Primary));
        }

        /// <summary>
        /// A fresh install has no config, so the whole rectangle arrives as zeros. That must
        /// land on the WALL. This is the exact shape of the bug spike finding 1 exists to warn
        /// about: 0,0 is the operator's desktop, and it survives Validate untouched because it
        /// genuinely does overlap the primary screen -- so the wall stays dark while a black
        /// rectangle sits on the wrong monitor, looking exactly like the app failed to start.
        /// </summary>
        [Fact]
        public void UnconfiguredGeometryResolvesOntoTheWallNotTheDesktop()
        {
            var fresh = new Rectangle(0, 0, 0, 0); // what a brand-new WallConfig holds

            var resolved = GeometryValidator.Resolve(fresh, TwoScreens, Primary);

            Assert.Equal(LedWall.X, resolved.X);
            Assert.False(Primary.IntersectsWith(resolved), "first run must not open on the operator's desktop");
        }

        [Fact]
        public void ResolveKeepsRealSavedGeometryIncludingDeliberateOverhang()
        {
            // 1964 deliberately exceeds the 1920 panel -- never clamped. See fact 2.
            var saved = new Rectangle(1920, 0, 1964, 256);
            Assert.Equal(saved, GeometryValidator.Resolve(saved, TwoScreens, Primary));
        }

        [Fact]
        public void ResolveFallsBackToPrimaryWhenItIsTheOnlyScreen()
        {
            var singleScreen = new[] { Primary };

            var resolved = GeometryValidator.Resolve(new Rectangle(0, 0, 0, 0), singleScreen, Primary);

            Assert.Equal(Primary.X, resolved.X);
            Assert.True(resolved.Width > 0 && resolved.Height > 0);
        }

        [Fact]
        public void GeometryOnAMissingScreenSnapsToPrimary()
        {
            var saved = new Rectangle(3840, 0, 1920, 256); // a third monitor, unplugged
            var result = GeometryValidator.Validate(saved, TwoScreens, Primary);

            Assert.True(Primary.IntersectsWith(result), "must land somewhere reachable");
            Assert.Equal(saved.Size, result.Size);
        }

        [Fact]
        public void PartiallyVisibleGeometryIsKept()
        {
            // Straddling the seam between screens is legitimate -- don't "helpfully" move it
            var saved = new Rectangle(1900, 0, 1920, 256);
            Assert.Equal(saved, GeometryValidator.Validate(saved, TwoScreens, Primary));
        }

        [Fact]
        public void ZeroSizedGeometryGetsUsableDefaults()
        {
            var result = GeometryValidator.Validate(new Rectangle(0, 0, 0, 0), TwoScreens, Primary);

            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }

        // -- Additions of our own, encoding the measured facts from the real machine --

        [Fact]
        public void WiderThanScreenGeometryIsNotClamped()
        {
            // Real wall geometry: the clip is 1964px wide, 44px wider than the 1920 panel.
            // At W=1920 VLC downscales and the wall looks soft; at W=1964 the overhang is
            // simply cropped and the visible area is pixel-for-pixel 1:1 and sharper.
            // Validate must NEVER clamp width to the screen's width for this reason.
            var saved = new Rectangle(1920, 0, 1964, 256);
            Assert.Equal(saved, GeometryValidator.Validate(saved, TwoScreens, Primary));
        }

        [Fact]
        public void DefaultGeometryPicksTheNonPrimaryDisplay()
        {
            // The LED wall must be the default target, not 0,0 -- mirroring was the
            // original (wrong) assumption; the real machine is an extended second display.
            var result = GeometryValidator.DefaultGeometry(TwoScreens, Primary);

            Assert.Equal(LedWall.X, result.X);
            Assert.NotEqual(Primary.X, result.X);
        }

        [Fact]
        public void DefaultGeometryFallsBackToPrimaryWithOnlyOneScreen()
        {
            // A single-monitor setup machine has no X=1920 display to land on -- the
            // operator must still get a reachable window on the one screen present.
            var oneScreen = new[] { Primary };
            var result = GeometryValidator.DefaultGeometry(oneScreen, Primary);

            Assert.Equal(Primary.X, result.X);
            Assert.Equal(Primary.Y, result.Y);
        }

        [Fact]
        public void DefaultGeometryIgnoresDeviceOrder()
        {
            // Pins the real-machine trap: DISPLAY1 (index 0) is the non-primary LED wall,
            // DISPLAY2 (index 1) is primary. Passing screens with the non-primary FIRST
            // must still choose it -- the decision comes from Primary-ness, never from
            // array position or device name/index.
            var nonPrimaryFirst = new[] { LedWall, Primary };
            var result = GeometryValidator.DefaultGeometry(nonPrimaryFirst, Primary);

            Assert.Equal(LedWall.X, result.X);
        }
    }
}
