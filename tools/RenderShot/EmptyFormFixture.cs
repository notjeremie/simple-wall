using System;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;
using SimpleWall.UI;
using System.IO;

namespace RenderShot
{
    /// <summary>
    /// A first run: no clips, nothing on the wall. This is the FIRST thing an operator ever sees,
    /// and the state most likely to be left un-looked-at because every fixture is more
    /// interesting. Transport should be visibly unavailable rather than dead.
    ///
    /// Usage: RenderShot.exe RenderShot.EmptyFormFixture artifacts\render\empty.png
    /// </summary>
    public static class EmptyFormFixture
    {
        public static Form Create()
        {
            var config = new WallConfig();
            var library = new ClipLibrary(config.Clips);
            var engine = new StubEngine();
            var thumbnails = new ThumbnailCache(Path.Combine(Path.GetTempPath(), "sw-rendershot-thumbs"));

            return new MainForm(engine, library, new Scheduler(config.Tasks), config, thumbnails);
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
