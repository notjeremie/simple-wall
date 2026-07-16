using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using LibVLCSharp.Shared;

namespace SimpleWall.Spike
{
    /// <summary>
    /// Risk-spike control window. Answers, in one VNC sitting, whether VLC 3.x can
    /// drive the Win7 LED wall: init, decode/loop, pixel-accurate output window,
    /// live brightness/contrast, and the black-frame gap on clip switch.
    ///
    /// Everything logs to spike-log.txt (path resolved at startup -- see
    /// SpikeLogPaths -- and shown in this window's title bar), because the only
    /// way evidence gets off the wall PC is as a file carried back over VNC.
    /// libvlc's own diagnostic log is written natively to vlc-log.txt alongside it
    /// (see CreateLibVlc) rather than bridged through a managed event handler.
    ///
    /// Throwaway: most of this is deleted in Task 9 once the real VlcWallEngine exists.
    /// </summary>
    public class SpikeForm : Form
    {
        private static readonly string[] VoutOptions = { "default", "direct3d9", "directdraw" };
        private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

        private readonly string _logDir;
        private readonly string _logPath;
        private readonly string _vlcLogPath;
        private readonly object _logLock = new object();

        private LibVLC _libVlc;
        private MediaPlayer _player;
        private OutputWindow _outputWindow;
        private string _currentSlot; // "A", "B", or null

        // Clip-switch timing. Both stopwatches are Restart()-ed together at the
        // moment Play is clicked, so their readings share the same zero point:
        //   GAP           -- click to MediaPlayer.Playing (state-machine transition)
        //   FIRST PICTURE -- click to _player.VoutCount > 0 (closer to an actual frame
        //                     hitting the output; Playing can fire before that).
        // Be honest in the runbook/findings about which is which -- they measure
        // different things and can legitimately differ.
        private readonly Stopwatch _gapStopwatch = new Stopwatch();
        private readonly Stopwatch _firstPictureStopwatch = new Stopwatch();
        private readonly Timer _firstPictureTimer;
        private string _switchFromSlot;

        // Controls referenced outside BuildUi
        private TextBox _clipATextBox;
        private TextBox _clipBTextBox;
        private NumericUpDown _xNumeric;
        private NumericUpDown _yNumeric;
        private NumericUpDown _wNumeric;
        private NumericUpDown _hNumeric;
        private TrackBar _brightnessTrackBar;
        private TrackBar _contrastTrackBar;
        private Label _brightnessValueLabel;
        private Label _contrastValueLabel;
        private CheckBox _softwareDecodeCheckBox;
        private ComboBox _voutComboBox;
        private TextBox _logTextBox;

        public SpikeForm()
        {
            _logDir = SpikeLogPaths.Directory;
            _logPath = Path.Combine(_logDir, "spike-log.txt");
            _vlcLogPath = Path.Combine(_logDir, "vlc-log.txt");

            // The log path goes in the title bar because the log IS the deliverable
            // of this trip -- if the operator can't find the file, the trip is wasted
            // even if everything else worked.
            Text = $"SimpleWall Spike -- VLC on Win7 probe -- log: {_logPath}";
            Width = 920;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(700, 500);

            // The output window is also TopMost (it has to be, to sit over other
            // windows on the LED-strip monitor). On a single, cramped VNC desktop
            // the two can overlap; this keeps the control window reachable so the
            // operator can still find Apply/Play/Stop instead of being locked out
            // by a black rectangle with no title bar.
            TopMost = true;

            _firstPictureTimer = new Timer { Interval = 5 };
            _firstPictureTimer.Tick += FirstPictureTimerTick;

            BuildUi();

            Load += (s, e) => InitializeVlc();
            FormClosing += (s, e) => ShutdownVlc();
        }

        // ----------------------------------------------------------------
        // UI construction
        // ----------------------------------------------------------------

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            var controlsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };
            root.Controls.Add(controlsPanel, 0, 0);

            controlsPanel.Controls.Add(BuildClipsGroup());
            controlsPanel.Controls.Add(BuildGeometryGroup());
            controlsPanel.Controls.Add(BuildImageAdjustGroup());
            controlsPanel.Controls.Add(BuildDecodeOptionsGroup());

            _logTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                MaxLength = 0, // unlimited -- the default 32767 cap silently stops accepting text mid-session
                Font = new Font(FontFamily.GenericMonospace, 8f)
            };
            root.Controls.Add(_logTextBox, 0, 1);
        }

        private GroupBox BuildClipsGroup()
        {
            var group = new GroupBox { Text = "Clips", Width = 880, AutoSize = true, Padding = new Padding(8) };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _clipATextBox = new TextBox { Dock = DockStyle.Fill };
            var browseA = new Button { Text = "Browse..." };
            browseA.Click += (s, e) => BrowseClip(_clipATextBox, "A");
            table.Controls.Add(new Label { Text = "Clip A:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            table.Controls.Add(_clipATextBox, 1, 0);
            table.Controls.Add(browseA, 2, 0);

            _clipBTextBox = new TextBox { Dock = DockStyle.Fill };
            var browseB = new Button { Text = "Browse..." };
            browseB.Click += (s, e) => BrowseClip(_clipBTextBox, "B");
            table.Controls.Add(new Label { Text = "Clip B:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            table.Controls.Add(_clipBTextBox, 1, 1);
            table.Controls.Add(browseB, 2, 1);

            var buttonRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var playA = new Button { Text = "Play A", Width = 90 };
            playA.Click += (s, e) => PlayClip("A", _clipATextBox.Text);
            var playB = new Button { Text = "Play B", Width = 90 };
            playB.Click += (s, e) => PlayClip("B", _clipBTextBox.Text);
            var stop = new Button { Text = "Stop", Width = 90 };
            stop.Click += (s, e) => StopPlayback();
            buttonRow.Controls.Add(playA);
            buttonRow.Controls.Add(playB);
            buttonRow.Controls.Add(stop);
            table.Controls.Add(buttonRow, 1, 2);

            group.Controls.Add(table);
            return group;
        }

        private GroupBox BuildGeometryGroup()
        {
            var group = new GroupBox { Text = "Output Geometry", Width = 880, AutoSize = true, Padding = new Padding(8) };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 5,
                AutoSize = true
            };

            _xNumeric = NewGeometryNumeric(-10000, 10000, 0);
            _yNumeric = NewGeometryNumeric(-10000, 10000, 0);
            _wNumeric = NewGeometryNumeric(1, 10000, 1920);
            _hNumeric = NewGeometryNumeric(1, 10000, 256);

            table.Controls.Add(NewLabel("X:"), 0, 0);
            table.Controls.Add(_xNumeric, 1, 0);
            table.Controls.Add(NewLabel("Y:"), 2, 0);
            table.Controls.Add(_yNumeric, 3, 0);

            table.Controls.Add(NewLabel("W:"), 0, 1);
            table.Controls.Add(_wNumeric, 1, 1);
            table.Controls.Add(NewLabel("H:"), 2, 1);
            table.Controls.Add(_hNumeric, 3, 1);

            var apply = new Button { Text = "Apply", Width = 90 };
            apply.Click += (s, e) => ApplyGeometry();
            table.Controls.Add(apply, 4, 0);

            group.Controls.Add(table);
            return group;
        }

        private static NumericUpDown NewGeometryNumeric(int min, int max, int value)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Width = 80
            };
        }

        private static Label NewLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 8, 3, 0) };
        }

        private GroupBox BuildImageAdjustGroup()
        {
            var group = new GroupBox { Text = "Image Adjust", Width = 880, AutoSize = true, Padding = new Padding(8) };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _brightnessTrackBar = new TrackBar { Minimum = 0, Maximum = 200, Value = 100, Dock = DockStyle.Fill, TickFrequency = 20 };
            _brightnessValueLabel = new Label { Text = "1.00", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, Width = 40 };
            var brightnessReset = new Button { Text = "Reset", Width = 70 };
            brightnessReset.Click += (s, e) => _brightnessTrackBar.Value = 100;
            _brightnessTrackBar.ValueChanged += (s, e) => ApplyBrightness();

            var contrastReset = new Button { Text = "Reset", Width = 70 };
            _contrastTrackBar = new TrackBar { Minimum = 0, Maximum = 200, Value = 100, Dock = DockStyle.Fill, TickFrequency = 20 };
            _contrastValueLabel = new Label { Text = "1.00", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, Width = 40 };
            contrastReset.Click += (s, e) => _contrastTrackBar.Value = 100;
            _contrastTrackBar.ValueChanged += (s, e) => ApplyContrast();

            table.Controls.Add(NewLabel("Brightness:"), 0, 0);
            table.Controls.Add(_brightnessTrackBar, 1, 0);
            table.Controls.Add(_brightnessValueLabel, 2, 0);
            table.Controls.Add(brightnessReset, 3, 0);

            table.Controls.Add(NewLabel("Contrast:"), 0, 1);
            table.Controls.Add(_contrastTrackBar, 1, 1);
            table.Controls.Add(_contrastValueLabel, 2, 1);
            table.Controls.Add(contrastReset, 3, 1);

            group.Controls.Add(table);
            return group;
        }

        private GroupBox BuildDecodeOptionsGroup()
        {
            var group = new GroupBox { Text = "Decode Options (Win7 fixes -- switchable without a rebuild)", Width = 880, AutoSize = true, Padding = new Padding(8) };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true
            };

            _softwareDecodeCheckBox = new CheckBox { Text = "Force software decode (:avcodec-hw=none)", AutoSize = true };
            table.Controls.Add(_softwareDecodeCheckBox, 0, 0);
            table.SetColumnSpan(_softwareDecodeCheckBox, 2);

            _voutComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            _voutComboBox.Items.AddRange(VoutOptions);
            _voutComboBox.SelectedIndex = 0;
            // Attach only after the initial selection so it doesn't fire during construction.
            _voutComboBox.SelectedIndexChanged += (s, e) =>
            {
                Log($"vout changed to '{_voutComboBox.SelectedItem}' -- recreating LibVLC instance. Playback stops; geometry is kept.");
                CreateLibVlc();
            };

            table.Controls.Add(NewLabel("vout:"), 0, 1);
            table.Controls.Add(_voutComboBox, 1, 1);

            group.Controls.Add(table);
            return group;
        }

        private void BrowseClip(TextBox target, string label)
        {
            using (var dialog = new OpenFileDialog { Filter = "MP4 files (*.mp4)|*.mp4|All files (*.*)|*.*", Title = $"Choose clip {label}" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.FileName;
                    Log($"Clip {label} set to: {dialog.FileName}");
                }
            }
        }

        // ----------------------------------------------------------------
        // Logging -- the single most important part of this spike.
        // ----------------------------------------------------------------

        private void Log(string message)
        {
            var line = $"{DateTime.Now.ToString(LogTimestampFormat, CultureInfo.InvariantCulture)} {message}";

            lock (_logLock)
            {
                try { File.AppendAllText(_logPath, line + Environment.NewLine); }
                catch { /* SpikeLogPaths already probed a writable directory at startup; this is a last-resort guard only */ }
            }

            if (_logTextBox == null || _logTextBox.IsDisposed) return;

            if (_logTextBox.InvokeRequired)
            {
                try { _logTextBox.BeginInvoke((Action)(() => AppendToPane(line))); }
                catch (ObjectDisposedException) { /* form closing race, ignore */ }
                catch (InvalidOperationException) { /* handle not created yet, ignore */ }
            }
            else
            {
                AppendToPane(line);
            }
        }

        private void AppendToPane(string line)
        {
            if (_logTextBox.IsDisposed) return;
            _logTextBox.AppendText(line + Environment.NewLine);
        }

        // ----------------------------------------------------------------
        // LibVLC lifecycle
        // ----------------------------------------------------------------

        private void InitializeVlc()
        {
            Log($"Log file:     {_logPath}");
            Log($"VLC log file: {_vlcLogPath}");
            Log("=== SimpleWall Spike starting ===");
            Log($"OS version: {Environment.OSVersion}");
            Log($"Process bitness: {(Environment.Is64BitProcess ? "x64" : "x86")}");
            Log($".NET runtime version: {Environment.Version}");

            CreateLibVlc();
        }

        /// <summary>
        /// (Re)creates the LibVLC instance, media player and output window.
        /// Called at startup and whenever the vout dropdown changes -- LibVLC options
        /// are only read at construction time, so a vout change means starting over.
        /// Playback stops when this runs; ApplyGeometry() below re-applies the saved
        /// X/Y/W/H immediately, so the window doesn't need re-positioning after a vout
        /// change, it just needs Play clicked again.
        /// </summary>
        private void CreateLibVlc()
        {
            ShutdownVlc();

            try
            {
                Core.Initialize();
                Log("Core.Initialize() succeeded.");
            }
            catch (Exception ex)
            {
                Log("FATAL: Core.Initialize() threw -- VLC cannot init on this machine:");
                Log(ex.ToString());
                MessageBox.Show(this, "Core.Initialize() failed. See spike-log.txt for the full exception.",
                    "Spike -- VLC init failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                var vout = _voutComboBox.SelectedItem as string ?? "default";
                var softwareDecode = _softwareDecodeCheckBox.Checked;

                var libVlcOptions = new List<string>();
                if (vout != "default") libVlcOptions.Add("--vout=" + vout);

                // libvlc's own diagnostic log, written natively -- not bridged through a
                // managed LibVLC.Log event handler. Unsubscribing a native->managed log
                // callback while one is in flight can throw AccessViolationException,
                // which .NET 4.8 does NOT route through AppDomain.UnhandledException --
                // the process would just die with zero evidence. This avoids that risk
                // entirely and is a richer artifact besides (full VLC verbosity, not
                // just what we chose to filter to warning/error).
                libVlcOptions.Add("--file-logging");
                libVlcOptions.Add("--logfile=" + _vlcLogPath);
                libVlcOptions.Add("--logmode=text");
                libVlcOptions.Add("--verbose=2");

                _libVlc = new LibVLC(libVlcOptions.ToArray());

                Log("==================================================");
                Log($"Run/recreate at {DateTime.Now.ToString(LogTimestampFormat, CultureInfo.InvariantCulture)}");
                Log($"LibVLC version: {_libVlc.Version}");
                Log($"vout={vout} softwareDecode={softwareDecode}");
                Log($"vlc-log.txt: {_vlcLogPath}");
                Log("==================================================");

                _player = new MediaPlayer(_libVlc);
                _player.Playing += (s, e) => MarshalPlayingToUiThread();
                _player.Stopped += (s, e) => Log($"MediaPlayer.Stopped (slot {_currentSlot}).");
                _player.EncounteredError += (s, e) => Log($"MediaPlayer.EncounteredError (slot {_currentSlot}).");

                // Do NOT Show() yet -- on a small/single-monitor VNC desktop this borderless
                // 1920x256 always-on-top rectangle would land directly over the Geometry
                // group and the Apply button before the operator ever gets a chance to move
                // it. It's shown for the first time in PlayClip(), by which point the
                // operator has already read the runbook step that tells them what to expect.
                _outputWindow = new OutputWindow(_player);
                ApplyGeometry();
                ApplyBrightness();
                ApplyContrast();
            }
            catch (Exception ex)
            {
                Log("FATAL: LibVLC/MediaPlayer construction threw:");
                Log(ex.ToString());
                MessageBox.Show(this, "LibVLC construction failed. See spike-log.txt for the full exception.",
                    "Spike -- VLC init failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Teardown order matters: stop the player first, then detach the VideoView's
        /// hwnd from it (set_hwnd(NULL) against a STOPPED player), only then dispose the
        /// view/window, then the player, then LibVLC. Disposing the output window before
        /// stopping the player used to hit set_hwnd(NULL) against a live player and could
        /// hang -- and this path runs on every vout change, which is exactly the moment
        /// the operator reaches for it because something already looks broken.
        /// </summary>
        private void ShutdownVlc()
        {
            _firstPictureTimer.Stop();

            if (_player != null)
            {
                try { _player.Stop(); } catch { /* best effort during teardown */ }
            }

            if (_outputWindow != null)
            {
                _outputWindow.DetachPlayer();
                _outputWindow.ShutDown();
                _outputWindow = null;
            }

            if (_player != null)
            {
                _player.Dispose();
                _player = null;
            }

            if (_libVlc != null)
            {
                _libVlc.Dispose();
                _libVlc = null;
            }
        }

        // ----------------------------------------------------------------
        // Playback
        // ----------------------------------------------------------------

        private void PlayClip(string slot, string path)
        {
            if (_player == null || _libVlc == null)
            {
                Log($"Play {slot} requested, but LibVLC is not initialized -- ignoring.");
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Log($"Play {slot} requested, but file not found: '{path}'");
                MessageBox.Show(this, "File not found: " + path, "Spike", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log($"--- Play {slot} requested: {path} ---");

            var media = new Media(_libVlc, path, FromType.FromPath);
            media.AddOption(":input-repeat=65535"); // loop indefinitely

            if (_softwareDecodeCheckBox.Checked)
            {
                media.AddOption(":avcodec-hw=none");
                Log("Software decode forced for this clip (:avcodec-hw=none).");
            }

            _switchFromSlot = _currentSlot;
            _currentSlot = slot;

            // Both stopwatches share this zero point -- see the field comments above
            // for what each one measures and why they're expected to differ.
            _gapStopwatch.Restart();
            _firstPictureStopwatch.Restart();
            _firstPictureTimer.Stop();
            _firstPictureTimer.Start();

            _player.Play(media);
            Log($"MediaPlayer.Play() called for slot {slot}.");

            _player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            ApplyBrightness();
            ApplyContrast();

            if (_outputWindow != null && !_outputWindow.Visible)
            {
                _outputWindow.Show();
                Log("Output window shown (first Play).");
            }
        }

        private void StopPlayback()
        {
            Log("--- Stop requested ---");
            _firstPictureTimer.Stop();
            _player?.Stop();
            _currentSlot = null;
        }

        /// <summary>
        /// VLC event callbacks run on libvlc's own thread, and libvlc's docs forbid
        /// re-entering libvlc from inside one of those callbacks -- it's a known
        /// deadlock source, and Playing is the success path: it fires the very first
        /// time anything works. So this callback does nothing itself except post a
        /// delegate to the UI thread and return immediately; all the actual work
        /// (which touches _player.Media / media.Tracks) happens over there instead.
        /// </summary>
        private void MarshalPlayingToUiThread()
        {
            try
            {
                if (IsHandleCreated) BeginInvoke((Action)OnPlayingOnUiThread);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnPlayingOnUiThread()
        {
            Log($"MediaPlayer.Playing (slot {_currentSlot}).");

            if (_gapStopwatch.IsRunning)
            {
                _gapStopwatch.Stop();
                var fromLabel = _switchFromSlot ?? "START";
                Log($"GAP {fromLabel}->{_currentSlot}: {_gapStopwatch.ElapsedMilliseconds} ms");
            }

            LogResolutionIfKnown();
        }

        /// <summary>
        /// Polls on a UI-thread timer (not a libvlc callback) until the player reports
        /// an active video output -- closer to "a frame is actually on the wall" than
        /// the Playing state-machine transition above, which can fire slightly before
        /// that. 5ms is what was asked for; real resolution is closer to Windows'
        /// ordinary timer granularity, but the Stopwatch reading taken at each tick is
        /// still far tighter than DateTime.Now's ~15.6ms granularity would give.
        /// </summary>
        private void FirstPictureTimerTick(object sender, EventArgs e)
        {
            if (_player != null && _player.VoutCount > 0)
            {
                _firstPictureTimer.Stop();
                Log($"FIRST PICTURE: {_firstPictureStopwatch.ElapsedMilliseconds} ms (slot {_currentSlot})");
            }
        }

        private void LogResolutionIfKnown()
        {
            try
            {
                var media = _player?.Media;
                if (media == null) return;

                foreach (var track in media.Tracks)
                {
                    if (track.TrackType == TrackType.Video)
                    {
                        Log($"Media resolution detected: {track.Data.Video.Width}x{track.Data.Video.Height}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Could not read media resolution: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // Geometry / brightness / contrast
        // ----------------------------------------------------------------

        private void ApplyGeometry()
        {
            var rect = new Rectangle((int)_xNumeric.Value, (int)_yNumeric.Value, (int)_wNumeric.Value, (int)_hNumeric.Value);
            Log($"Geometry apply: X={rect.X} Y={rect.Y} W={rect.Width} H={rect.Height}");
            _outputWindow?.SetGeometry(rect);
        }

        private void ApplyBrightness()
        {
            var value = _brightnessTrackBar.Value / 100f;
            _brightnessValueLabel.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
            if (_player == null) return;
            _player.SetAdjustFloat(VideoAdjustOption.Brightness, value);
            Log($"Brightness set to {value.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        private void ApplyContrast()
        {
            var value = _contrastTrackBar.Value / 100f;
            _contrastValueLabel.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
            if (_player == null) return;
            _player.SetAdjustFloat(VideoAdjustOption.Contrast, value);
            Log($"Contrast set to {value.ToString("0.00", CultureInfo.InvariantCulture)}");
        }
    }
}
