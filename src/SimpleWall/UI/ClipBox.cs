using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SimpleWall.UI
{
    /// <summary>
    /// One clip in the grid: its first frame, its filename, and its slot number.
    ///
    /// Dumb on purpose. It draws what it is told and raises events; it never touches the engine.
    /// MainForm owns that, because the grid must show what the WALL is doing rather than what the
    /// mouse asked for -- a box that lit itself on click would lie the moment OSC or the
    /// scheduler changed the clip.
    /// </summary>
    public class ClipBox : Control
    {
        public const int ThumbWidth = 160;
        public const int ThumbHeight = 90;
        public const int BoxWidth = ThumbWidth + 8;
        public const int BoxHeight = ThumbHeight + 32;

        private static readonly Color Idle = Color.FromArgb(60, 60, 64);
        private static readonly Color PlayingBorder = Color.FromArgb(0, 200, 255);
        private static readonly Color MissingBorder = Color.FromArgb(220, 60, 60);
        private static readonly Color Backing = Color.FromArgb(32, 32, 36);
        private static readonly Color DefaultGold = Color.FromArgb(255, 205, 60);

        private bool _isPlaying;
        private bool _isMissing;
        private bool _isDefault;
        private Image _thumbnail;

        public ClipBox(int slot, string path)
        {
            Slot = slot;
            Path = path;
            Size = new Size(BoxWidth, BoxHeight);
            Margin = new Padding(6);
            BackColor = Backing;
            Cursor = Cursors.Hand;
            AllowDrop = true;
            DoubleBuffered = true;
            TabStop = false;
        }

        public int Slot { get; }
        public string Path { get; }

        /// <summary>Set from the engine's state, never from this control's own click.</summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set { if (_isPlaying == value) return; _isPlaying = value; Invalidate(); }
        }

        public bool IsMissing
        {
            get => _isMissing;
            set { if (_isMissing == value) return; _isMissing = value; Invalidate(); }
        }

        /// <summary>The clip the wall boots into. Drawn as a gold star; set from config, not click.</summary>
        public bool IsDefault
        {
            get => _isDefault;
            set { if (_isDefault == value) return; _isDefault = value; Invalidate(); }
        }

        public Image Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; Invalidate(); }
        }

        public event EventHandler Triggered;
        public event EventHandler RemoveRequested;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left) Triggered?.Invoke(this, EventArgs.Empty);
        }

        internal void RequestRemove() => RemoveRequested?.Invoke(this, EventArgs.Empty);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.Clear(Backing);

            var thumb = new Rectangle(4, 4, ThumbWidth, ThumbHeight);
            DrawThumbnail(g, thumb);
            DrawSlotBadge(g, thumb);
            DrawDefaultBadge(g, thumb);
            DrawFilename(g);
            DrawBorder(g);
        }

        /// <summary>
        /// A gold star in the top-right of the thumbnail (opposite the slot number) when this is
        /// the clip the wall boots into. Drawn as a filled polygon, NOT a glyph: the Win7 wall PC
        /// may not have a font with U+2605, and a missing glyph would silently draw nothing --
        /// exactly the kind of invisible failure this project keeps getting bitten by.
        /// </summary>
        private void DrawDefaultBadge(Graphics g, Rectangle thumb)
        {
            if (!_isDefault) return;

            var badge = new Rectangle(thumb.Right - 22, thumb.Y, 22, 22);
            using (var brush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(brush, badge);

            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(DefaultGold))
                g.FillPolygon(brush, StarPoints(badge.X + badge.Width / 2f, badge.Y + badge.Height / 2f, 9f, 3.8f));
            g.SmoothingMode = smoothing;
        }

        private static PointF[] StarPoints(float cx, float cy, float outer, float inner)
        {
            var points = new PointF[10];
            for (var i = 0; i < 10; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var r = (i % 2 == 0) ? outer : inner;
                points[i] = new PointF((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
            }
            return points;
        }

        private void DrawThumbnail(Graphics g, Rectangle thumb)
        {
            if (_thumbnail != null && !_isMissing)
            {
                g.DrawImage(_thumbnail, thumb);
                return;
            }

            using (var brush = new SolidBrush(Color.FromArgb(20, 20, 22)))
                g.FillRectangle(brush, thumb);

            // Says which of the two silences this is: a thumbnail still being made in the
            // background, or a clip that isn't there at all.
            var caption = _isMissing ? "FILE MISSING" : "…";
            using (var brush = new SolidBrush(_isMissing ? MissingBorder : Color.FromArgb(110, 110, 116)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(caption, Font, brush, thumb, format);
        }

        private void DrawSlotBadge(Graphics g, Rectangle thumb)
        {
            var badge = new Rectangle(thumb.X, thumb.Y, 26, 18);
            using (var brush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(brush, badge);
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Slot.ToString(), Font, Brushes.White, badge, format);
        }

        private void DrawFilename(Graphics g)
        {
            var name = string.IsNullOrEmpty(Path) ? "(no file)" : System.IO.Path.GetFileName(Path);

            // Inset clear of the border: at 3px (playing/missing) a flush-left string touches the
            // frame and the descenders sit on the bottom edge. Caught by looking at the render.
            var area = new Rectangle(8, ThumbHeight + 8, ThumbWidth - 8, 16);

            // EllipsisPath, not EllipsisCharacter: these names are long and the distinguishing
            // part is at the END (WALL_BEFORE_SUNSET_1964X256), so trimming the tail would leave
            // a column of boxes that all read the same.
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
            using (var brush = new SolidBrush(_isMissing ? MissingBorder : Color.FromArgb(210, 210, 214)))
                g.DrawString(name, Font, brush, area, format);
        }

        private void DrawBorder(Graphics g)
        {
            var colour = _isMissing ? MissingBorder : _isPlaying ? PlayingBorder : Idle;
            var thickness = _isPlaying || _isMissing ? 3 : 1;

            using (var pen = new Pen(colour, thickness))
            {
                var inset = thickness / 2f;
                g.DrawRectangle(pen, inset, inset, Width - thickness, Height - thickness);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // The thumbnail Image is owned by MainForm's cache and shared between boxes -- a box
            // must not dispose it, or rebuilding the grid would leave the others drawing a
            // disposed bitmap.
            base.Dispose(disposing);
        }
    }
}
