using System.Drawing;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace SimpleWall.Spike
{
    /// <summary>
    /// Borderless, always-on-top window that hosts the VLC VideoView.
    /// This is the window the operator drags/sizes onto the LED strip.
    /// Spike-only: the real product recreates this shape in Task 9's OutputWindow.
    /// </summary>
    public class OutputWindow : Form
    {
        private readonly VideoView _videoView;
        private bool _allowClose;

        public OutputWindow(MediaPlayer player)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;

            _videoView = new VideoView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                MediaPlayer = player
            };
            Controls.Add(_videoView);

            FormClosing += OnFormClosing;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                // Borderless and always-on-top: there is no legitimate way for the
                // operator to close this window directly. If it ever happens anyway
                // (e.g. a stray Alt+F4 while it somehow has focus), refuse -- otherwise
                // SpikeForm is left holding a non-null but disposed _outputWindow, and
                // the next ApplyGeometry() throws ObjectDisposedException on the UI thread.
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Detaches the VideoView from the player. Must be called while the player
        /// is already stopped, and before either side is disposed -- see the ordering
        /// in SpikeForm.ShutdownVlc. Calling set_hwnd(NULL) against a live player is
        /// the hang this exists to avoid.
        /// </summary>
        public void DetachPlayer()
        {
            _videoView.MediaPlayer = null;
        }

        /// <summary>Moves and resizes the window live. Called from SpikeForm's Apply button.</summary>
        public void SetGeometry(Rectangle rect)
        {
            Bounds = rect;
        }

        /// <summary>
        /// The only sanctioned way to close this window -- used during real shutdown
        /// (app exit, or recreating LibVLC on a vout change). Anything else hitting
        /// FormClosing is refused; see OnFormClosing.
        /// </summary>
        public void ShutDown()
        {
            _allowClose = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _videoView.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
