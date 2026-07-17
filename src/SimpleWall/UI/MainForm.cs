using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;

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
        private readonly Scheduler _scheduler;
        private readonly WallConfig _config;
        private readonly ThumbnailCache _thumbnails;
        private readonly Action _saveConfig;
        private readonly Action<string> _log;

        private readonly FlowLayoutPanel _grid;
        private readonly Label _status;
        private readonly Button _addTile;
        private readonly ContextMenuStrip _boxMenu;
        private readonly ToolStripMenuItem _removeItem;
        private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Button _playPause;
        private Button _stop;
        private TrackBar _brightness;
        private TrackBar _contrast;
        private Label _brightnessValue;
        private Label _contrastValue;
        private readonly Timer _saveDebounce = new Timer { Interval = 800 };
        private readonly Timer _tick = new Timer { Interval = 1000 };
        private SchedulerTab _schedulerTab;

        /// <summary>
        /// THE no-catch-up rule, and it is this one line: it starts at "now", so a task whose
        /// moment fell before the app started is simply never inside a window the scheduler is
        /// asked about. Boot at 13:20 and the 13:00 task does not run -- nothing unexpected ever
        /// appears on the wall while nobody is in the room to see it was wrong.
        /// </summary>
        private DateTime _previousTick = DateTime.Now;

        private ClipBox _dragSource;
        private ClipBox _menuTarget;
        private Point _dragOrigin;
        private string _notice;

        public MainForm(IWallEngine engine, ClipLibrary library, Scheduler scheduler, WallConfig config,
            ThumbnailCache thumbnails, Action saveConfig = null, Action<string> log = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
            _saveConfig = saveConfig ?? (() => { });
            _log = log ?? (_ => { });

            Text = "SimpleWall";

            // Wide enough for five tiles and the + across, with room to spare. At 920 it missed
            // by two pixels and orphaned the + onto a row of its own, which looks broken rather
            // than full. Five is not sacred -- it wraps on resize, as it should -- but the
            // default size should show an arrangement that looks deliberate.
            ClientSize = new Size(960, 600);
            MinimumSize = new Size(560, 360);
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
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 24,
                ForeColor = Color.FromArgb(170, 170, 176),
                Padding = new Padding(8, 5, 8, 0),
                Text = "Drop .mp4 files here, or press +"
            };

            _boxMenu = new ContextMenuStrip();
            _removeItem = new ToolStripMenuItem("Remove clip");
            BuildBoxMenu();

            _saveDebounce.Tick += (s, e) => { _saveDebounce.Stop(); SaveConfig(); };
            _tick.Tick += OnSchedulerTick;
            _tick.Start();

            // An explicit three-row table rather than three Dock'd controls. Docking to the same
            // edge resolves by z-order, which is the sort of thing that reads fine and comes out
            // in the wrong order -- and this project has already shipped one window whose layout
            // nobody could see. Rows: grid fills, controls and status take what they need.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(24, 24, 28)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildTabs(), 0, 0);
            root.Controls.Add(BuildControlBar(), 0, 1);
            root.Controls.Add(_status, 0, 2);
            Controls.Add(root);

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
        // Tabs and the scheduler tick
        // ----------------------------------------------------------------

        private Control BuildTabs()
        {
            _schedulerTab = new SchedulerTab(_scheduler, _library, SaveConfig) { Dock = DockStyle.Fill };

            var clips = new TabPage("Clips") { BackColor = Color.FromArgb(24, 24, 28) };
            clips.Controls.Add(_grid);

            var schedule = new TabPage("Schedule") { BackColor = Color.FromArgb(24, 24, 28) };
            schedule.Controls.Add(_schedulerTab);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(clips);
            tabs.TabPages.Add(schedule);

            // The schedule's sentences name clips, and its red "this cannot fire" marks depend on
            // the roster, so it must be re-read whenever the operator might have changed it rather
            // than only when the schedule itself changes.
            tabs.SelectedIndexChanged += (s, e) =>
            {
                if (tabs.SelectedTab == schedule) _schedulerTab.Refresh_();
            };

            return tabs;
        }

        /// <summary>
        /// One second, on the UI thread, same as everything else that touches the engine.
        ///
        /// Wrapped, because this runs unattended for months: one throw here would silently stop
        /// the schedule for the rest of the session and nobody would notice until something didn't
        /// appear on the wall on a Sunday.
        /// </summary>
        private void OnSchedulerTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;

            try
            {
                if (TickGuard.ShouldResync(_previousTick, now))
                {
                    _log($"Clock moved: {_previousTick:HH:mm:ss} -> {now:HH:mm:ss}. " +
                         "Skipping that window rather than firing everything in it.");
                    return; // finally still resyncs _previousTick
                }

                if (!_scheduler.Enabled) return;

                foreach (var task in _scheduler.DueBetween(_previousTick, now))
                    Fire(task);
            }
            catch (Exception ex)
            {
                _log("Scheduler tick failed: " + ex);
            }
            finally
            {
                // Advanced whatever happened -- a throw, a clock jump, or the schedule being off.
                // Otherwise re-enabling would fire everything missed while it was switched off,
                // which is the catch-up this design deliberately does not do.
                _previousTick = now;
            }
        }

        /// <summary>
        /// One task, isolated.
        ///
        /// Per task, not per tick: DueBetween hands back every task due in the window AND has
        /// already marked each due one-off as Spent. A single throw from Execute -- a clip on a
        /// share that just dropped is enough -- would abandon every remaining task in that window,
        /// and no-catch-up means they never run. A one-off would be burned for a fire that never
        /// happened.
        ///
        /// Logged AFTER Execute returns, because "Scheduler fired" printed before the attempt is a
        /// line that can be a lie.
        /// </summary>
        private void Fire(ScheduledTask task)
        {
            try
            {
                _engine.Execute(task.Command);
                _log($"Scheduler fired: {task.Describe(NameOfClip)}");

                // A fired one-off must not fire again after a restart, and Spent only says so if it
                // reaches disk. Rare enough to save on the spot.
                if (task.OneOffDate.HasValue) SaveConfig();
            }
            catch (Exception ex)
            {
                _log($"Scheduler task failed ({task.Describe(NameOfClip)}): {ex.Message}");
            }
        }

        private string NameOfClip(int slot)
        {
            var clip = _library.BySlot(slot);
            if (clip == null) return null;
            return string.IsNullOrEmpty(clip.Path) ? "(no file)" : System.IO.Path.GetFileName(clip.Path);
        }

        // ----------------------------------------------------------------
        // Transport and image adjustment
        // ----------------------------------------------------------------

        private Control BuildControlBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 3,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.FromArgb(32, 32, 36)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var transport = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            _playPause = NewButton("Pause", (s, e) => Command(WallCommand.Simple(CommandKind.Toggle)));
            _stop = NewButton("Stop", (s, e) => Command(WallCommand.Simple(CommandKind.Stop)));
            transport.Controls.Add(_playPause);
            transport.Controls.Add(_stop);
            bar.Controls.Add(transport, 0, 0);
            bar.SetColumnSpan(transport, 4);

            _brightness = NewAdjustBar(CommandKind.Brightness);
            _brightnessValue = NewValueLabel();
            AddAdjustRow(bar, 1, "Brightness:", _brightness, _brightnessValue, CommandKind.Brightness);

            _contrast = NewAdjustBar(CommandKind.Contrast);
            _contrastValue = NewValueLabel();
            AddAdjustRow(bar, 2, "Contrast:", _contrast, _contrastValue, CommandKind.Contrast);

            SyncAdjustFromConfig();
            return bar;
        }

        private void AddAdjustRow(TableLayoutPanel bar, int row, string caption, TrackBar slider, Label value, CommandKind kind)
        {
            bar.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 206),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 6, 0)
            }, 0, row);
            bar.Controls.Add(slider, 1, row);
            bar.Controls.Add(value, 2, row);
            bar.Controls.Add(NewButton("Reset", (s, e) => SetAdjust(kind, AdjustValue.Neutral)), 3, row);
        }

        private Button NewButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(72, 26),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 226),
                BackColor = Color.FromArgb(48, 48, 54),
                Margin = new Padding(0, 2, 6, 2)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
            button.Click += onClick;
            return button;
        }

        private static Label NewValueLabel() => new Label
        {
            Text = "1.00",
            AutoSize = false,
            Width = 40,
            ForeColor = Color.FromArgb(200, 200, 206),
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(6, 8, 6, 0)
        };

        /// <summary>
        /// TrackBars are integers, so 0-200 stands in for 0.0-2.0. 100 is neutral.
        /// </summary>
        private TrackBar NewAdjustBar(CommandKind kind)
        {
            var slider = new WallTrackBar
            {
                Minimum = 0,
                Maximum = 200,
                Value = 100,
                TickFrequency = 20,
                Dock = DockStyle.Fill
            };

            // Scroll is user-initiated ONLY, which is exactly what should reach the wall.
            // Setting Value in code raises ValueChanged but never Scroll, so syncing the slider
            // from the engine can't echo a command back at it.
            slider.Scroll += (s, e) =>
            {
                Command(WallCommand.WithValue(kind, slider.Value / 100f));
                SaveSoon();
            };
            slider.ValueChanged += (s, e) => UpdateAdjustLabels();
            return slider;
        }

        private void SetAdjust(CommandKind kind, float value)
        {
            Command(WallCommand.WithValue(kind, value));

            // Explicit, because a stub engine that raises no StateChanged (RenderShot's fixtures)
            // would otherwise leave the slider showing the old value. Against the real engine
            // this is a harmless second sync.
            SyncAdjustFromConfig();
            SaveSoon();
        }

        /// <summary>
        /// One save path for every route in: drag, keyboard, Reset. ConfigStore.Save is an atomic
        /// file write and a drag fires Scroll a hundred times, so this waits for the operator to
        /// stop moving instead of writing per tick.
        ///
        /// A debounce rather than saving on mouse-up, because "release" is not a thing every input
        /// has -- the mouse wheel raises Scroll with no MouseUp and no KeyUp at all, so a
        /// release-based save silently dropped every wheel-driven change on the floor.
        /// </summary>
        private void SaveSoon()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        /// <summary>
        /// The sliders show what the WALL is set to, not what was last dragged -- same rule as
        /// the grid. OSC or a preset can change brightness without this window's knowledge, and
        /// a slider sitting at the wrong value is a small lie that costs an operator real time.
        /// </summary>
        private void SyncAdjustFromConfig()
        {
            SyncSlider(_brightness, _config.Brightness);
            SyncSlider(_contrast, _config.Contrast);
            UpdateAdjustLabels();
        }

        /// <summary>
        /// Skips only the slider actually being dragged, and asks Windows rather than tracking it
        /// ourselves.
        ///
        /// A flag set on MouseDown and cleared on MouseUp has two failure modes this doesn't: it
        /// suppresses BOTH sliders, so an OSC change to contrast would sit unshown while
        /// brightness is dragged; and it can latch. Capture is broken without a MouseUp when a
        /// system-modal window appears or the session changes -- and this machine is reachable
        /// only over VNC, where a dropped client mid-drag is an ordinary Tuesday. That flag would
        /// then be true for the life of the process and the sliders would never track the wall
        /// again, silently. Capture is Windows' own state and cannot latch that way.
        /// </summary>
        private static void SyncSlider(TrackBar slider, float value)
        {
            if (slider.Capture) return;
            slider.Value = ToSlider(value);
        }

        /// <summary>
        /// Through the same clamp the engine uses, not a re-derived one. Re-deriving is how
        /// `(int)Math.Round(NaN * 100)` becomes int.MinValue and floors the slider to 0 while the
        /// wall runs at 2.0 -- the readout saying the exact opposite of the truth. A config
        /// holding an ordinary-looking 1e40 overflows float to infinity and gets there.
        /// </summary>
        private static int ToSlider(float value) => (int)Math.Round(AdjustValue.Clamp(value) * 100f);

        private void UpdateAdjustLabels()
        {
            _brightnessValue.Text = (_brightness.Value / 100f).ToString("0.00", CultureInfo.InvariantCulture);
            _contrastValue.Text = (_contrast.Value / 100f).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void RefreshTransport()
        {
            _playPause.Text = _engine.IsPlaying ? "Pause" : "Play";

            // Nothing loaded means Toggle and Stop would do nothing. A button that does nothing
            // is worse than a button that is visibly unavailable.
            var loaded = _engine.CurrentSlot != null;
            _playPause.Enabled = loaded;
            _stop.Enabled = loaded;
        }

        /// <summary>Everything the operator does goes through the engine. Nothing takes a shortcut.</summary>
        private void Command(WallCommand command)
        {
            _notice = null;
            _engine.Execute(command);
        }

        private void SaveConfig()
        {
            try
            {
                _saveConfig();
            }
            catch (Exception ex)
            {
                _log("Saving config failed: " + ex.Message);
            }
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

            RefreshTransport();
            SyncAdjustFromConfig();
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

            // A first run has nothing to report and everything to explain. "0 clip(s). Nothing on
            // the wall." is true, useless, and the first thing the operator ever reads.
            if (_library.Clips.Count == 0)
            {
                _status.Text = "No clips yet -- drop video files here, or press +";
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
                _saveDebounce.Dispose();
                _tick.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
