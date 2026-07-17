using System;
using Microsoft.Win32;
using SimpleWall.Infrastructure;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Against the REAL registry, under a value name of this test's own. That is the point: the
    /// only interesting question about this class is whether Windows agrees with it, and a mocked
    /// registry cannot answer that. The production value name ("SimpleWall") is never touched, so
    /// running the suite on the wall PC cannot switch its autostart off.
    ///
    /// See Task 15: whether the registry value ACTUALLY brings the app back is a reboot, not a
    /// unit test. This proves the value is written the way Windows expects to read it.
    /// </summary>
    public class AutostartTests : IDisposable
    {
        private readonly string _valueName = "SimpleWall-test-" + Guid.NewGuid().ToString("N");
        private readonly Autostart _autostart;

        public AutostartTests() => _autostart = new Autostart(_valueName);

        public void Dispose()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Autostart.RunKey, writable: true))
                    key?.DeleteValue(_valueName, throwOnMissingValue: false);
            }
            catch { }
        }

        private string RawValue()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Autostart.RunKey))
                return key?.GetValue(_valueName) as string;
        }

        [Fact]
        public void StartsOff()
        {
            Assert.False(_autostart.IsEnabled());
            Assert.Null(_autostart.RegisteredPath());
        }

        [Fact]
        public void EnablingWritesTheRunValueAndDisablingRemovesIt()
        {
            _autostart.Set(true, @"C:\wall\SimpleWall.exe");
            Assert.True(_autostart.IsEnabled());

            _autostart.Set(false, null);

            Assert.False(_autostart.IsEnabled());
            Assert.Null(RawValue());
        }

        /// <summary>
        /// Unticking a box that was never ticked is not an error, and must not throw at the
        /// operator. DeleteValue's default is to throw on a missing value.
        /// </summary>
        [Fact]
        public void DisablingWhenAlreadyOffIsFine()
        {
            var ex = Record.Exception(() => _autostart.Set(false, null));

            Assert.Null(ex);
        }

        [Fact]
        public void EnablingTwiceOverwritesRatherThanDuplicating()
        {
            _autostart.Set(true, @"C:\old\SimpleWall.exe");
            _autostart.Set(true, @"C:\new\SimpleWall.exe");

            Assert.Equal(@"C:\new\SimpleWall.exe", _autostart.RegisteredPath());
        }

        /// <summary>
        /// The path is quoted in the registry, because any ordinary install has a space in it
        /// ("C:\Program Files\...") and Windows splits an unquoted Run value on spaces -- it would
        /// try to launch "C:\Program". Measured against what actually lands in the key, not against
        /// what we think we wrote.
        /// </summary>
        [Fact]
        public void ThePathIsQuotedInTheRegistry()
        {
            _autostart.Set(true, @"C:\Program Files\SimpleWall\SimpleWall.exe");

            Assert.Equal("\"C:\\Program Files\\SimpleWall\\SimpleWall.exe\"", RawValue());
        }

        /// <summary>
        /// ...and reading it back gives the path, not the quotes. Otherwise every comparison
        /// against Application.ExecutablePath fails and the tab claims autostart points elsewhere
        /// when it points exactly here.
        /// </summary>
        [Fact]
        public void ReadingBackStripsTheQuotes()
        {
            _autostart.Set(true, @"C:\Program Files\SimpleWall\SimpleWall.exe");

            Assert.Equal(@"C:\Program Files\SimpleWall\SimpleWall.exe", _autostart.RegisteredPath());
            Assert.True(_autostart.PointsAt(@"C:\Program Files\SimpleWall\SimpleWall.exe"));
        }

        /// <summary>
        /// The case the whole RegisteredPath/PointsAt pair exists for: the app was copied
        /// somewhere else, so autostart is genuinely ON and genuinely will not start THIS one.
        /// A tick box cannot say that; the settings tab has to.
        /// </summary>
        [Fact]
        public void AutostartCanBeOnAndStillNotPointAtUs()
        {
            _autostart.Set(true, @"C:\somewhere\else\SimpleWall.exe");

            Assert.True(_autostart.IsEnabled());
            Assert.False(_autostart.PointsAt(@"C:\wall\SimpleWall.exe"));
        }

        /// <summary>Windows paths are case-insensitive; so is this, or it cries wolf on every check.</summary>
        [Fact]
        public void PointsAtIgnoresCaseAndPathShape()
        {
            _autostart.Set(true, @"c:\wall\bin\..\SimpleWall.exe");

            Assert.True(_autostart.PointsAt(@"C:\Wall\SimpleWall.exe"));
        }

        [Fact]
        public void PointsAtIsFalseWhenAutostartIsOff()
        {
            Assert.False(_autostart.PointsAt(@"C:\wall\SimpleWall.exe"));
        }

        /// <summary>
        /// A hand-edited Run value can hold anything at all. Asking "does autostart point at us"
        /// must answer no, not throw from the constructor of the window that would have shown it.
        /// </summary>
        [Fact]
        public void PointsAtSurvivesAValueThatIsNotAPath()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Autostart.RunKey))
                key.SetValue(_valueName, "not|a|path\0<>");

            var ex = Record.Exception(() => Assert.False(_autostart.PointsAt(@"C:\wall\SimpleWall.exe")));

            Assert.Null(ex);
        }

        [Fact]
        public void EnablingWithNoPathIsRefusedRatherThanWritingRubbish()
        {
            Assert.Throws<ArgumentException>(() => _autostart.Set(true, ""));
        }
    }
}
