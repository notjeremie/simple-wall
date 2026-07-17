using System;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Infrastructure;
using SimpleWall.Model;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The settings tab's one job is to never lie about the machine. These tests drive the real
    /// control and read the status labels, because the status text IS the feature: a port box and
    /// a status line that disagree is precisely the afternoon-wasting bug this exists to prevent.
    ///
    /// The registry is faked here (a fake Autostart), deliberately: Autostart's own tests prove
    /// the real registry round-trip, and this tab's logic -- on/off/points-elsewhere -- is what
    /// wants exercising without touching HKCU.
    /// </summary>
    public class SettingsTabTests
    {
        private class FakeAutostart : Autostart
        {
            public bool Enabled;
            public string Path;
            public bool ThrowOnSet;

            public override bool IsEnabled() => Enabled;
            public override string RegisteredPath() => Enabled ? Path : null;
            public override bool PointsAt(string exePath) =>
                Enabled && string.Equals(Path, exePath, StringComparison.OrdinalIgnoreCase);
            public override void Set(bool enabled, string exePath)
            {
                if (ThrowOnSet) throw new InvalidOperationException("registry said no");
                Enabled = enabled;
                Path = enabled ? exePath : null;
            }
        }

        private static Label Label(Control root, string name) => Find<Label>(root, name);
        private static NumericUpDown Numeric(Control root, string name) => Find<NumericUpDown>(root, name);
        private static CheckBox Check(Control root, string name) => Find<CheckBox>(root, name);

        private static T Find<T>(Control root, string name) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T typed && child.Name == name) return typed;
                var nested = Find<T>(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>Forces the whole control tree into existence without showing the window --
        /// the same trick RenderShot uses; a tab never selected otherwise never lays out.</summary>
        private static SettingsTab Realize(SettingsTab tab)
        {
            using (var form = new Form())
            {
                form.Controls.Add(tab);
                var h = form.Handle;
                GC.KeepAlive(h);
                form.Controls.Remove(tab);
            }
            return tab;
        }

        private static SettingsTab NewTab(WallConfig config, FakeAutostart autostart = null, string exePath = null) =>
            Realize(new SettingsTab(config, null, null, autostart ?? new FakeAutostart(),
                exePath ?? @"C:\wall\SimpleWall.exe"));

        [Fact]
        public void ReportsTheBoundPortAsListening()
        {
            using (var tab = NewTab(new WallConfig { OscPort = 7000 }))
            {
                tab.SetOscStatus(7000, null);

                Assert.Contains("7000", Label(tab, "oscStatus").Text);
                Assert.Contains("Listening", Label(tab, "oscStatus").Text);
            }
        }

        [Fact]
        public void SaysOscIsOffWhenItFailedToBind()
        {
            using (var tab = NewTab(new WallConfig { OscPort = 7000 }))
            {
                tab.SetOscStatus(-1, "OSC port 7000 could not be opened (AddressAlreadyInUse).");

                Assert.Contains("could not be opened", Label(tab, "oscStatus").Text);
            }
        }

        /// <summary>
        /// The heart of the "port box is a wish" problem: the socket is bound and cannot be
        /// rebound, so editing the port must produce a restart hint, and the status must keep
        /// naming the port ACTUALLY in use rather than the one just typed.
        /// </summary>
        [Fact]
        public void EditingThePortAsksForARestartAndKeepsNamingTheRealPort()
        {
            var config = new WallConfig { OscPort = 7000 };
            using (var tab = NewTab(config))
            {
                tab.SetOscStatus(7000, null);

                Numeric(tab, "oscPort").Value = 7001;

                var status = Label(tab, "oscStatus").Text;
                Assert.Contains("Restart", status);
                Assert.Contains("7000", status);       // still the real one
                Assert.Equal(7001, config.OscPort);    // but the edit did reach the config
            }
        }

        [Fact]
        public void EditingTheReplyHostAlsoAsksForARestart()
        {
            var config = new WallConfig { OscPort = 7000, OscReplyHost = "" };
            using (var tab = NewTab(config))
            {
                tab.SetOscStatus(7000, null);

                Find<TextBox>(tab, "replyHost").Text = "streamdeck-pc";

                Assert.Contains("Restart", Label(tab, "oscStatus").Text);
                Assert.Equal("streamdeck-pc", config.OscReplyHost);
            }
        }

        /// <summary>
        /// A configured port of 0 binds to an arbitrary free one, so the bound port legitimately
        /// differs from what was configured. That must NOT read as "restart to apply" -- the
        /// operator changed nothing.
        /// </summary>
        [Fact]
        public void PortZeroBindingElsewhereIsNotMistakenForAPendingChange()
        {
            using (var tab = NewTab(new WallConfig { OscPort = 0 }))
            {
                tab.SetOscStatus(53124, null);

                Assert.DoesNotContain("Restart", Label(tab, "oscStatus").Text);
            }
        }

        /// <summary>
        /// A hand-edited config.json (deliberately not range-validated) can hold an OscPort of
        /// 70000. The box can only show 65535, and the socket in Program is bound from the config
        /// AFTER this tab reconciles it -- so the config must be normalised to what is shown, and
        /// that normalisation must NOT then read as an unsaved "restart to apply" change.
        /// </summary>
        [Fact]
        public void AnOutOfRangePortIsNormalisedIntoConfigAndNotFlaggedAsPending()
        {
            var config = new WallConfig { OscPort = 70000 };
            using (var tab = NewTab(config))
            {
                Assert.Equal(65535, config.OscPort);                      // reconciled into config
                Assert.Equal(65535m, Numeric(tab, "oscPort").Value);      // and that is what shows

                tab.SetOscStatus(65535, null);                            // the socket bound the clamped value

                Assert.DoesNotContain("Restart", Label(tab, "oscStatus").Text);
                Assert.Contains("Listening on port 65535", Label(tab, "oscStatus").Text);
            }
        }

        [Fact]
        public void ShowsAutostartOffWhenTheRegistryHasNothing()
        {
            using (var tab = NewTab(new WallConfig(), new FakeAutostart { Enabled = false }))
            {
                Assert.False(Check(tab, "autostart").Checked);
                Assert.Contains("will not start", Label(tab, "autostartStatus").Text);
            }
        }

        [Fact]
        public void ShowsAutostartOnWhenItPointsAtUs()
        {
            var autostart = new FakeAutostart { Enabled = true, Path = @"C:\wall\SimpleWall.exe" };
            using (var tab = NewTab(new WallConfig(), autostart, @"C:\wall\SimpleWall.exe"))
            {
                Assert.True(Check(tab, "autostart").Checked);
                Assert.Contains("will start", Label(tab, "autostartStatus").Text);
            }
        }

        /// <summary>
        /// The case a tick box cannot express: autostart is genuinely on, and genuinely will not
        /// start THIS copy. The tab must warn and name the other path.
        /// </summary>
        [Fact]
        public void WarnsWhenAutostartPointsAtADifferentCopy()
        {
            var autostart = new FakeAutostart { Enabled = true, Path = @"C:\old\SimpleWall.exe" };
            using (var tab = NewTab(new WallConfig(), autostart, @"C:\wall\SimpleWall.exe"))
            {
                var status = Label(tab, "autostartStatus").Text;
                Assert.Contains("different copy", status);
                Assert.Contains(@"C:\old\SimpleWall.exe", status);
            }
        }

        [Fact]
        public void TickingTheBoxWritesAutostartForThisExe()
        {
            var autostart = new FakeAutostart { Enabled = false };
            using (var tab = NewTab(new WallConfig(), autostart, @"C:\wall\SimpleWall.exe"))
            {
                Check(tab, "autostart").Checked = true;

                Assert.True(autostart.Enabled);
                Assert.Equal(@"C:\wall\SimpleWall.exe", autostart.Path);
            }
        }

        /// <summary>
        /// A registry write that fails must not leave the box showing a change that did not
        /// happen -- that is the same silent lie in miniature. It snaps back to the truth.
        /// </summary>
        [Fact]
        public void AFailedAutostartWriteSnapsTheBoxBack()
        {
            var autostart = new FakeAutostart { Enabled = false, ThrowOnSet = true };
            using (var tab = NewTab(new WallConfig(), autostart))
            {
                Check(tab, "autostart").Checked = true;

                Assert.False(Check(tab, "autostart").Checked);
                Assert.Contains("Could not change autostart", Label(tab, "autostartStatus").Text);
            }
        }

        /// <summary>
        /// The Task 13 crash, guarded here too: config.json is not range-validated, so an
        /// out-of-range OutputX would throw from NumericUpDown.Value -- i.e. from this
        /// constructor, before Application.Run exists to catch it. It must be clamped, not thrown.
        /// </summary>
        [Fact]
        public void AnOutOfRangeGeometryFromConfigDoesNotCrashTheConstructor()
        {
            var config = new WallConfig { OutputX = 999999, OutputY = -999999, OutputWidth = 1964, OutputHeight = 256 };

            var ex = Record.Exception(() =>
            {
                using (NewTab(config)) { }
            });

            Assert.Null(ex);
        }

        [Fact]
        public void GeometryBoxesReflectTheConfig()
        {
            var config = new WallConfig { OutputX = 1920, OutputY = 0, OutputWidth = 1964, OutputHeight = 256 };
            using (var tab = NewTab(config))
            {
                Assert.Equal(1920, Numeric(tab, "x").Value);
                Assert.Equal(1964, Numeric(tab, "width").Value);
                Assert.Equal(256, Numeric(tab, "height").Value);
            }
        }
    }
}
