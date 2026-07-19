using System;
using System.IO;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Infrastructure;
using SimpleWall.Model;
using SimpleWall.Scheduling;
using SimpleWall.UI;

namespace RenderShot
{
    /// <summary>
    /// The Settings tab in its HEALTHY state: OSC listening on the configured port, autostart on
    /// and pointing at this copy, real geometry filled in. This is the picture that says "nothing
    /// is wrong", and it still has to be looked at -- a tab that renders clean here and broken in
    /// SettingsWarningFixture is the pair that proves the layout, not either alone.
    ///
    /// Usage: RenderShot.exe RenderShot.SettingsFixture artifacts\render\settings.png
    /// </summary>
    public static class SettingsFixture
    {
        public static Form Create() => Build(warning: false);

        internal static Form Build(bool warning)
        {
            var config = new WallConfig
            {
                OscPort = 7000,
                OscReplyHost = "streamdeck-pc",
                OscReplyPort = 9000,
                OutputX = 1920,
                OutputY = 0,
                OutputWidth = 1964,
                OutputHeight = 256
            };

            var library = new ClipLibrary(config.Clips);
            var engine = new StubEngine();
            var thumbnails = new ThumbnailCache(Path.Combine(Path.GetTempPath(), "sw-rendershot-thumbs"));

            // The whole reason these two states differ. Healthy: autostart on, pointing here.
            // Warning: autostart on, but pointing at a copy that isn't this one.
            var exePath = @"C:\wall\SimpleWall.exe";
            var autostart = new FakeAutostart
            {
                Enabled = true,
                Path = warning ? @"C:\Users\operator\Desktop\old-build\SimpleWall.exe" : exePath
            };

            var form = new MainForm(engine, library, new Scheduler(config.Tasks), config, thumbnails,
                null, null, null, autostart, exePath);

            SelectSettingsTab(form);

            // Only the warning fixture pushes OSC into a state worth showing: a port edited after
            // the socket was bound, so the "restart to apply" line is on screen. The healthy one
            // shows the plain "listening" line.
            var settings = FindSettingsTab(form);
            if (warning)
            {
                settings.SetOscStatus(7000, null);
                config.OscPort = 7001;                 // as if the operator just typed it
                settings.SetOscStatus(7000, null);     // recompute against the now-changed config
            }
            else
            {
                settings.SetOscStatus(7000, null);
            }

            return form;
        }

        private static void SelectSettingsTab(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TabControl tabs && tabs.TabPages.Count > 2)
                {
                    tabs.SelectedIndex = 2;
                    return;
                }
                SelectSettingsTab(child);
            }
        }

        private static SettingsTab FindSettingsTab(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is SettingsTab tab) return tab;
                var nested = FindSettingsTab(child);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>A registry that answers from memory, so a render never touches HKCU.</summary>
        private class FakeAutostart : Autostart
        {
            public bool Enabled;
            public string Path;

            public override bool IsEnabled() => Enabled;
            public override string RegisteredPath() => Enabled ? Path : null;
            public override bool PointsAt(string exePath) =>
                Enabled && string.Equals(Path, exePath, StringComparison.OrdinalIgnoreCase);
            public override void Set(bool enabled, string exePath) { Enabled = enabled; Path = enabled ? exePath : null; }
        }

        private class StubEngine : IWallEngine
        {
            public int? CurrentSlot => null;
            public bool IsPlaying => false;
            public float CurrentBrightness => AdjustValue.Neutral;
            public float CurrentContrast => AdjustValue.Neutral;

#pragma warning disable 67 // never raised: a render is a still life
            public event EventHandler StateChanged;
            public event EventHandler<ClipUnavailableEventArgs> ClipUnavailable;
#pragma warning restore 67

            public void Execute(WallCommand command) { }
        }
    }
}
