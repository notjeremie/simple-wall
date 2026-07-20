using System;
using System.IO;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;
using SimpleWall.UI;

namespace RenderShot
{
    /// <summary>
    /// Builds a MainForm worth looking at: several clips, one of them PLAYING and one of them
    /// MISSING, so a single render shows every visual state the operator will ever see rather
    /// than just the happy one.
    ///
    /// Usage: RenderShot.exe RenderShot.MainFormFixture artifacts\render\mainform.png
    /// </summary>
    public static class MainFormFixture
    {
        public static Form Create() => Build(currentSlot: 2);

        public static Form Build(int? currentSlot)
        {
            // A real file, so those boxes are in their normal state rather than red. The
            // thumbnails stay as placeholders: extraction is async by design and RenderShot has
            // no message loop to deliver the continuation, which is exactly what the operator
            // sees for the first moment after launch anyway.
            var real = FindFixtureClip();

            var config = new WallConfig();
            var library = new ClipLibrary(config.Clips);
            library.Add(real);                                    // slot 1
            library.Add(real);                                    // slot 2 -- shown playing below
            library.Add(@"D:\clips\missing-clip.mp4"); // slot 3 -- missing
            library.Add(real);                                    // slot 4

            // The playing clip carries a non-neutral look, so the render proves the sliders read
            // the CLIP's saved brightness/contrast (not a global) -- brightness well below the
            // midpoint, contrast above it, both plainly off-centre in the picture.
            library.BySlot(2).Brightness = 0.55f;
            library.BySlot(2).Contrast = 1.35f;

            var engine = new StubEngine { CurrentSlot = currentSlot, IsPlaying = currentSlot != null };
            var thumbnails = new ThumbnailCache(Path.Combine(Path.GetTempPath(), "sw-rendershot-thumbs"));

            return new MainForm(engine, library, new Scheduler(config.Tasks), config, thumbnails);
        }

        /// <summary>
        /// Walks up to the repo root rather than counting "..". The first version of this counted,
        /// got it off by one, and every box in the render came out red -- which the picture showed
        /// instantly and the layout tree did not.
        /// </summary>
        private static string FindFixtureClip()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "red-then-blue-1964x256.mp4");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "The fixture clip is missing, so every box would render as FILE MISSING and the " +
                "render would be worthless. Expected tests/fixtures/red-then-blue-1964x256.mp4.");
        }

        /// <summary>Reports a fixed state and does nothing. The wall is not involved in a render.</summary>
        private class StubEngine : IWallEngine
        {
            public int? CurrentSlot { get; set; }
            public bool IsPlaying { get; set; }

            // Irrelevant to the render -- MainForm reads the clip's look from the library, not the
            // engine -- but the interface requires them.
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
