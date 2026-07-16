using System.Drawing;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace SimpleWall.UI
{
    /// <summary>
    /// The window that sits on the LED strip. Borderless, always-on-top, black.
    ///
    /// It holds TWO VideoViews stacked on top of each other, not one. That is the whole
    /// point of this class. Measured on the real wall, the black frame between clips was
    /// ~290ms (GAP A->B 112ms, FIRST PICTURE 286ms) and plainly visible; the design had
    /// originally cut layers assuming the black was brief. It isn't. So the incoming clip
    /// loads on the back layer while the outgoing one still holds the wall, and the swap
    /// is a z-order flip once the incoming layer actually has a picture.
    ///
    /// Still one clip at a time. Layers are an implementation detail, not a feature -- no
    /// crossfade, no mixer, nothing to configure.
    ///
    /// The layers swap by z-order rather than by Visible, deliberately: both VideoViews are
    /// opaque and fill the window, so the front one completely hides the back one, and a
    /// VideoView that is never visible is not a shape libvlc's vout has been tested against
    /// on this machine. Hiding a window is exactly the kind of change that works on a
    /// desktop and produces a black wall on a Radeon HD 7800 running Win7.
    ///
    /// Layout note: built entirely in the constructor, nothing in Load. RenderShot can't
    /// instantiate this one (it needs two live MediaPlayers, and it's a black rectangle
    /// anyway -- there is nothing to look at), but Task 10's control window must follow the
    /// same rule, and that one RenderShot very much can render.
    /// </summary>
    public class OutputWindow : Form
    {
        private readonly VideoView _layerA;
        private readonly VideoView _layerB;
        private bool _allowClose;

        public OutputWindow(MediaPlayer playerA, MediaPlayer playerB)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Text = "SimpleWall output";

            _layerA = NewLayer(playerA);
            _layerB = NewLayer(playerB);

            Controls.Add(_layerB);
            Controls.Add(_layerA);

            // Then say it explicitly rather than relying on the order above. Controls.Add
            // APPENDS, and child index 0 is the front, so adding B first actually leaves B in
            // front -- the opposite of what reading this constructor suggests. VlcWallEngine
            // starts at _frontIsA = true, and if that disagrees with the real z-order the
            // engine stops the wrong player and shows a layer that never played anything: a
            // black wall, no exception, no log line. Measured, not assumed.
            _layerA.BringToFront();

            FormClosing += OnFormClosing;
        }

        private static VideoView NewLayer(MediaPlayer player) => new VideoView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            MediaPlayer = player
        };

        /// <summary>
        /// The hard cut. Brings the named layer in front of the other one, which is what the
        /// operator sees as the clip changing. Call this only once the incoming layer has a
        /// picture -- calling it early is precisely the ~290ms of black this class exists to
        /// prevent.
        /// </summary>
        public void BringLayerToFront(bool layerA) => (layerA ? _layerA : _layerB).BringToFront();

        /// <summary>Moves and resizes the window live, from settings.</summary>
        public void SetGeometry(Rectangle bounds) => Bounds = bounds;

        /// <summary>
        /// Detaches both VideoViews from their players. Must be called while the players are
        /// already stopped and before either side is disposed. Calling set_hwnd(NULL) against
        /// a live player is a hang, and it hangs on the wall PC, headless, at 3am.
        /// </summary>
        public void DetachPlayers()
        {
            _layerA.MediaPlayer = null;
            _layerB.MediaPlayer = null;
        }

        /// <summary>The only sanctioned way to close this window. See OnFormClosing.</summary>
        public void ShutDown()
        {
            _allowClose = true;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose) return;

            // Windows logging off or shutting down, and the operator ending the process from
            // Task Manager, must NOT be blocked -- refusing those surfaces as "this program
            // is preventing shutdown" on the wall PC, which is worse than what this guard
            // protects against.
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
                return;

            // Borderless and always-on-top: there is no legitimate way for the operator to
            // close this directly. If it happens anyway (a stray Alt+F4 while it somehow has
            // focus), refuse -- otherwise the engine is left holding a disposed window and
            // the next SetGeometry throws on the UI thread.
            e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _layerA.Dispose();
                _layerB.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
