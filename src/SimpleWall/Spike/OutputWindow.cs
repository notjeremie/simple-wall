using System;
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
        }

        /// <summary>Moves and resizes the window live. Called from SpikeForm's Apply button.</summary>
        public void SetGeometry(Rectangle rect)
        {
            Bounds = rect;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _videoView.MediaPlayer = null;
                _videoView.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
