using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;

namespace SimpleWall.UI
{
    /// <summary>
    /// The control window: the grid of clips the operator actually touches.
    ///
    /// NOT always-on-top (the output window is). The operator needs to be able to put this
    /// behind other windows on the wall PC's desktop.
    ///
    /// Two rules, both learned the hard way:
    ///
    ///   1. **The whole control tree is built in the constructor, never in Load.** Anything
    ///      created in Load is invisible to tools/RenderShot, which is the only way anyone can
    ///      look at this window before it reaches the wall -- and RenderShot cannot tell the
    ///      difference, so it would report a clean render of a window missing half its controls.
    ///      The spike shipped with every GroupBox collapsed to a 16px sliver precisely because
    ///      nobody could see it.
    ///   2. **The grid repaints from the engine, never from the click handler.** Refresh() reads
    ///      _engine.CurrentSlot. A box that lit itself on click would be lying the moment a
    ///      Stream Deck button or the scheduler changed the clip -- and being lied to about
    ///      what is on the wall is the one thing an operator cannot recover from.
    /// </summary>
    public class MainForm : Form
    {
        private readonly IWallEngine _engine;
        private readonly ClipLibrary _library;
        private readonly ThumbnailCache _thumbnails;
        private readonly Action<string> _log;

        private readonly FlowLayoutPanel _grid;
        private readonly Label _status;
        private readonly Button _addTile;
        private readonly ContextMenuStrip _boxMenu;
        private readonly ToolStripMenuItem _removeItem;
        private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ClipBox _dragSource;
        private ClipBox _menuTarget;
        private Point _dragOrigin;
        private string _notice;

        public MainForm(IWallEngine engine, ClipLibrary library, ThumbnailCache thumbnails, Action<string> log = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
            _log = log ?? (_ => { });

            Text = "SimpleWall";
            ClientSize = new Size(920, 560);
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 24, 28);
            AllowDrop = true;

            _grid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(24, 24, 28),
                AllowDrop = true
            };
            _grid.DragEnter += OnDragEnter;
            _grid.DragDrop += OnDropOnGrid;

            _addTile = new Button
            {
                Text = "+",
                Size = new Size(ClipBox.BoxWidth, ClipBox.BoxHeight),
                Margin = new Padding(6),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 20f),
                ForeColor = Color.FromArgb(150, 150, 156),
                BackColor = Color.FromArgb(32, 32, 36)
            };
            _addTile.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 64);
            _addTile.Click += (s, e) => BrowseForClips();

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                ForeColor = Color.FromArgb(170, 170, 176),
                Padding = new Padding(8, 5, 8, 0),
                Text = "Drop .mp4 files here, or press +"
            };

            _boxMenu = new ContextMenuStrip();
            _removeItem = new ToolStripMenuItem("Remove clip");
            BuildBoxMenu();

            Controls.Add(_grid);
            Controls.Add(_status);

            DragEnter += OnDragEnter;
            DragDrop += OnDropOnGrid;

            _engine.StateChanged += (s, e) => BeginInvokeSafely(RefreshGrid);
            _engine.ClipUnavailable += (s, e) => BeginInvokeSafely(() => ReportUnavailable(e));

            BuildGrid();

            // The other half of ClipLibrary's contract: it repairs a config with duplicate or
            // out-of-range slots, and its own docs say this window is what tells the operator.
            // A silent renumber means a Stream Deck button quietly triggers a different clip.
            if (_library.WasNormalized)
                SetNotice("config.json had duplicate or out-of-range slots and was repaired -- " +
                          "check the slot numbers against your Stream Deck.");
        }

        // ----------------------------------------------------------------
        // Grid
        // ----------------------------------------------------------------

        /// <summary>
        /// Rebuilds the boxes from the library. Called on any change to the roster; a plain
        /// state change only needs <see cref="RefreshGrid"/>.
        /// </summary>
        private void BuildGrid()
        {
            // Every box is about to be disposed, so anything pointing at one is now a dangling
            // reference to a dead control.
            _dragSource = null;
            _menuTarget = null;

            _grid.SuspendLayout();
            foreach (var box in _grid.Controls.OfType<ClipBox>().ToList())
            {
                _grid.Controls.Remove(box);
                box.Dispose();
            }
            _grid.Controls.Remove(_addTile);

            foreach (var clip in _library.Clips.ToList())
                _grid.Controls.Add(NewBox(clip));

            // The + tile goes away at the ceiling, because Add() would return null and the
            // operator would be left clicking a button that does nothing.
            if (_library.Clips.Count < ClipLibrary.MaxClips)
                _grid.Controls.Add(_addTile);

            _grid.ResumeLayout();
            RefreshGrid();
            RequestThumbnails();
        }

        private ClipBox NewBox(ClipEntry clip)
        {
            var box = new ClipBox(clip.Slot, clip.Path)
            {
                // Checked on every rebuild, not just at startup: a clip can vanish from the
                // share at any time, and a box that still looks fine is an invitation to press
                // a button that does nothing.
                IsMissing = !ClipExists(clip.Path)
            };

            if (_images.TryGetValue(clip.Path ?? "", out var image)) box.Thumbnail = image;

            box.Triggered += (s, e) => Trigger(clip.Slot);
            box.RemoveRequested += (s, e) => RemoveClip(clip.Slot);
            box.ContextMenuStrip = _boxMenu;
            box.MouseDown += OnBoxMouseDown;
            box.MouseMove += OnBoxMouseMove;
            box.DragEnter += OnDragEnter;
            box.DragDrop += (s, e) => OnDropOnBox(box, e);
            return box;
        }

        /// <summary>
        /// ONE menu for the whole grid, retargeted as it opens.
        ///
        /// A menu per box leaks: Control.Dispose does NOT dispose an assigned ContextMenuStrip,
        /// it only unhooks it -- and BuildGrid recreates every box on every roster change and
        /// every ClipUnavailable. Any menu the operator actually opened would leak its window
        /// handle permanently, on a machine that runs for months.
        ///
        /// SourceControl is captured in Opening rather than read in Click, because by the time
        /// the item is clicked the menu has closed and SourceControl is null.
        /// </summary>
        private void BuildBoxMenu()
        {
            _boxMenu.Opening += (s, e) =>
            {
                _menuTarget = _boxMenu.SourceControl as ClipBox;
                if (_menuTarget == null) { e.Cancel = true; return; }
                _removeItem.Text = "Remove clip " + _menuTarget.Slot;
            };

            // A menu item rather than plain right-click-to-delete: right-click is far too easy
            // to do by accident, and there is no undo here.
            _removeItem.Click += (s, e) => _menuTarget?.RequestRemove();
            _boxMenu.Items.Add(_removeItem);
        }

        /// <summary>
        /// Repaints from the ENGINE's state. Never from a click handler -- see the class docs.
        /// </summary>
        private void RefreshGrid()
        {
            foreach (var box in _grid.Controls.OfType<ClipBox>())
                box.IsPlaying = _engine.CurrentSlot == box.Slot && _engine.IsPlaying;

            UpdateStatus();
        }

        /// <summary>
        /// A notice outranks the state line and stays until the operator's next action.
        ///
        /// Without this, every error this UI can produce is erased before it can be read: the
        /// error paths set the status and then call BuildGrid, which repaints and overwrites it
        /// on the very next line. "Slot 3: file not found" became "3 clip(s). Wall: slot 2."
        /// instantly, and the clip-unavailable event -- the engine's ONLY way to tell an
        /// operator that the button they pressed did nothing -- was thrown away at the last step.
        /// </summary>
        private void UpdateStatus()
        {
            if (_notice != null)
            {
                _status.Text = _notice;
                return;
            }

            var playing = _engine.CurrentSlot;
            _status.Text = playing == null
                ? $"{_library.Clips.Count} clip(s). Nothing on the wall."
                : $"{_library.Clips.Count} clip(s). Wall: slot {playing}{(_engine.IsPlaying ? "" : " (paused)")}.";
        }

        private void SetNotice(string text)
        {
            _notice = text;
            UpdateStatus();
        }

        private void Trigger(int slot)
        {
            // The operator has acted, so whatever they were being told about is now stale.
            _notice = null;
            _engine.Execute(WallCommand.PlayClip(slot));
        }

        private void ReportUnavailable(ClipUnavailableEventArgs e)
        {
            _log($"UI: slot {e.Slot} unavailable ({e.Reason})");
            BuildGrid();
            SetNotice($"Slot {e.Slot}: {e.Reason}. The wall is unchanged.");
        }

        // ----------------------------------------------------------------
        // Roster changes
        // ----------------------------------------------------------------

        private void AddClips(IEnumerable<string> paths)
        {
            var added = 0;
            var rejected = 0;

            string notice = null;

            foreach (var path in paths)
            {
                if (!IsClipFile(path)) { rejected++; continue; }
                if (_library.Add(path) == null)
                {
                    notice = $"Only {ClipLibrary.MaxClips} slots exist -- some clips were not added.";
                    break;
                }
                added++;
            }

            if (rejected > 0 && added == 0)
                notice = "Those don't look like video files.";

            // BuildGrid first, notice second: the other way round the rebuild's repaint wipes
            // the message. At the 50-clip ceiling this used to silently swallow the extra files.
            if (added > 0) BuildGrid();
            if (notice != null) SetNotice(notice);
        }

        private void RemoveClip(int slot)
        {
            // Removing what is on the wall would leave the grid showing nothing highlighted
            // while the wall kept playing -- the exact desync this UI is built to avoid.
            if (_engine.CurrentSlot == slot) _engine.Execute(WallCommand.Simple(CommandKind.Stop));

            _library.Remove(slot);
            _log($"UI: removed slot {slot}");
            BuildGrid();
        }

        private void BrowseForClips()
        {
            using (var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Video files|*.mp4;*.mov;*.avi;*.mkv;*.m4v|All files|*.*"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddClips(dialog.FileNames);
            }
        }

        // ----------------------------------------------------------------
        // Drag and drop: files in from Explorer, boxes around to reorder
        // ----------------------------------------------------------------

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else if (e.Data.GetDataPresent(typeof(ClipBox))) e.Effect = DragDropEffects.Move;
            else e.Effect = DragDropEffects.None;
        }

        private void OnDropOnGrid(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                AddClips((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private void OnDropOnBox(ClipBox target, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                AddClips((string[])e.Data.GetData(DataFormats.FileDrop));
                return;
            }

            if (!e.Data.GetDataPresent(typeof(ClipBox))) return;
            var source = (ClipBox)e.Data.GetData(typeof(ClipBox));
            if (source == null || source == target) return;

            var from = IndexOfSlot(source.Slot);
            var to = IndexOfSlot(target.Slot);
            if (from < 0 || to < 0) return;

            // Order only. Slot numbers travel with their clip -- see ClipLibrary.Move.
            _library.Move(from, to);
            _log($"UI: moved slot {source.Slot} to position {to + 1}");
            BuildGrid();
        }

        private void OnBoxMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragSource = (ClipBox)sender;
            _dragOrigin = e.Location;
        }

        private void OnBoxMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSource == null || e.Button != MouseButtons.Left) return;

            // Only start a drag once the mouse has actually travelled. Without this, the tiny
            // movement in an ordinary click turns every trigger into a drag and the operator
            // can't start a clip.
            var moved = Math.Abs(e.X - _dragOrigin.X) > SystemInformation.DragSize.Width ||
                        Math.Abs(e.Y - _dragOrigin.Y) > SystemInformation.DragSize.Height;
            if (!moved) return;

            var source = _dragSource;
            _dragSource = null;
            source.DoDragDrop(source, DragDropEffects.Move);
        }

        // ----------------------------------------------------------------
        // Thumbnails
        // ----------------------------------------------------------------

        /// <summary>
        /// Kicks off extraction in the background and never waits for it. The grid is already on
        /// screen and usable; pictures arrive when they arrive.
        /// </summary>
        private async void RequestThumbnails()
        {
            foreach (var clip in _library.Clips.ToList())
            {
                // Stop, don't skip. This used to `continue` on a disposed form, so closing the
                // window kept feeding fresh extractions into a ThumbnailCache that Program.Main
                // was already disposing -- turning a race into a routine.
                if (IsDisposed) return;

                var path = clip.Path;
                if (string.IsNullOrEmpty(path) || !ClipExists(path)) continue;

                // _inFlight, not just _images: BuildGrid can start a second loop while this one
                // is awaiting, and both would extract the same clip and assign _images[path] --
                // orphaning the first Image, which nothing then disposes.
                if (_images.ContainsKey(path) || !_inFlight.Add(path)) continue;

                try
                {
                    var png = await _thumbnails.GetAsync(path);
                    if (png == null || IsDisposed) continue;

                    // Loaded via a copied stream, not Image.FromFile, which keeps the file
                    // locked for the life of the Image and would stop the cache ever replacing
                    // a stale thumbnail.
                    Image image;
                    using (var stream = new MemoryStream(File.ReadAllBytes(png)))
                        image = Image.FromStream(stream);

                    _images[path] = image;
                    foreach (var box in _grid.Controls.OfType<ClipBox>().Where(b => b.Path == path))
                        box.Thumbnail = image;
                }
                catch (Exception ex)
                {
                    // A missing picture is not worth a dialog, or a crash.
                    _log($"Thumbnail failed for {path}: {ex.Message}");
                }
                finally
                {
                    // Released even on failure, so a clip that fails once can be retried on a
                    // later rebuild rather than being written off for the session.
                    _inFlight.Remove(path);
                }
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private int IndexOfSlot(int slot)
        {
            var clips = _library.Clips;
            for (var i = 0; i < clips.Count; i++)
                if (clips[i].Slot == slot) return i;
            return -1;
        }

        private static bool ClipExists(string path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path);

        private static bool IsClipFile(string path)
        {
            if (!ClipExists(path)) return false;
            var extension = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".mp4" || extension == ".mov" || extension == ".avi" ||
                   extension == ".mkv" || extension == ".m4v";
        }

        /// <summary>
        /// The engine raises StateChanged on the UI thread today, but OSC (Task 12) will not, and
        /// a wrong answer there is a cross-thread crash on a machine nobody is watching.
        /// </summary>
        private void BeginInvokeSafely(Action action)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var image in _images.Values) image.Dispose();
                _images.Clear();
                _addTile.Dispose();
                _boxMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
