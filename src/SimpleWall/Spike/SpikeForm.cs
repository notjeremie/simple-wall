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
    /// libvlc's own diagnostic log is written natively via LibVLC.SetLogFile,
    /// called once per LibVLC instance -- NOT via --file-logging/--logfile/
    /// --logmode construction options, which are VLC 2.x-only and make
    /// libvlc_new() return NULL on 3.x (verified: see CreateLibVlc). Each
    /// (re)creation gets its own uniquely-named vlc-log file, because
    /// SetLogFile truncates and step 9 of the runbook deliberately recreates
    /// LibVLC several times to compare vout options.
    ///
    /// Throwaway: most of this is deleted in Task 9 once the real VlcWallEngine exists.
    /// </summary>
    public class SpikeForm : Form
    {
        private static readonly string[] VoutOptions = { "default", "direct3d9", "directdraw" };
        private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private static readonly TimeSpan FirstPictureTimeout = TimeSpan.FromSeconds(10);

        private readonly string _logDir;
        private readonly string _logPath;
        private readonly object _logLock = new object();
        private StreamWriter _logWriter;

        private LibVLC _libVlc;
        private MediaPlayer _player;
        private OutputWindow _outputWindow;
        private string _currentSlot; // "A", "B", or null
        private int _libVlcInstanceIndex;
        private string _currentVlcLogPath;

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
            _logWriter = OpenLogWriter(out _logDir, out _logPath);
            SpikeLogPaths.ActiveLogDirectory = _logDir;

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
            FormClosing += (s, e) => OnFormClosingCleanup();
        }

        /// <summary>
        /// Tries to open spike-log.txt itself (not just probe the directory) in each
        /// candidate directory in turn, keeping the first one that actually opens.
        /// A directory being writable doesn't guarantee the specific file isn't
        /// locked by something else -- this is the only way to find that out.
        /// FileShare.ReadWrite so Program.cs's crash handler (a completely separate
        /// FileStream, opened later, possibly mid-crash) can still append to the
        /// same file while this handle is held open for the whole session.
        /// </summary>
        private static StreamWriter OpenLogWriter(out string resolvedDirectory, out string resolvedPath)
        {
            foreach (var candidate in SpikeLogPaths.CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    var path = Path.Combine(candidate, "spike-log.txt");
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    var writer = new StreamWriter(stream) { AutoFlush = true };
                    resolvedDirectory = candidate;
                    resolvedPath = path;
                    return writer;
                }
                catch
                {
                    // This candidate's spike-log.txt is unusable (locked, permissions,
                    // whatever) -- try the next directory rather than silently losing
                    // every line for the rest of the session.
                }
            }

            resolvedDirectory = AppDomain.CurrentDomain.BaseDirectory;
            resolvedPath = Path.Combine(resolvedDirectory, "spike-log.txt");
            return null; // Log() below tolerates a null writer and just updates the log pane.
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
                try { _logWriter?.WriteLine(line); }
                catch { /* OpenLogWriter already tried every candidate directory/file at startup; this is a last-resort guard only */ }
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
            Log($"Log file: {_logPath}");
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

                // NOTE: --file-logging / --logfile=... / --logmode=text are VLC 2.x
                // construction options. They do NOT exist in VLC 3.x, and libvlc treats
                // an unknown option as FATAL rather than ignoring it -- passing them
                // makes libvlc_new() return NULL, i.e. `new LibVLC(...)` throws here.
                // Verified directly against the shipped 3.0.21 binary: plugins\logger\
                // contains only libconsole_logger_plugin.dll (no file logger plugin),
                // and none of those three option strings appear anywhere in libvlc.dll,
                // libvlccore.dll, or any of the 432 plugins. The real 3.x mechanism is
                // LibVLC.SetLogFile(path), called AFTER construction -- see below.
                var libVlcOptions = new List<string> { "--verbose=2" };
                if (vout != "default") libVlcOptions.Add("--vout=" + vout);

                _libVlc = new LibVLC(libVlcOptions.ToArray());

                // SetLogFile TRUNCATES on open, so each (re)creation gets its own
                // uniquely-named file -- otherwise runbook step 8's default -> direct3d9
                // -> directdraw sequence would leave only the LAST attempt's log,
                // destroying exactly the failing-configuration evidence that step exists
                // to collect.
                _libVlcInstanceIndex++;
                _currentVlcLogPath = Path.Combine(_logDir, $"vlc-log-{_libVlcInstanceIndex}-{vout}.txt");
                _libVlc.SetLogFile(_currentVlcLogPath);

                Log("==================================================");
                Log($"Run/recreate at {DateTime.Now.ToString(LogTimestampFormat, CultureInfo.InvariantCulture)}");
                Log($"LibVLC version: {_libVlc.Version}");
                Log($"vout={vout} softwareDecode={softwareDecode}");
                Log($"vlc log file: {_currentVlcLogPath}");
                Log("Note: libvlc rescans all bundled plugins on every (re)creation -- this");
                Log("package ships no plugin cache. On a slow disk this can take several");
                Log("seconds; that's normal, not a hang.");
                Log("==================================================");

                _player = new MediaPlayer(_libVlc);
                _player.Playing += OnLibVlcPlaying;
                _player.Stopped += (s, e) => Log($"MediaPlayer.Stopped (slot {_currentSlot}).");
                _player.EncounteredError += (s, e) => Log($"MediaPlayer.EncounteredError (slot {_currentSlot}).");

                // Do NOT Show() yet -- on a small/single-monitor VNC desktop this borderless
                // 1920x256 always-on-top rectangle would land directly over the Geometry
                // group and the Apply button before the operator ever gets a chance to move
                // it. It's shown for the first time in PlayClip(), before Play() is called --
                // see the comment there for why that ordering matters.
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
        /// view/window, then close libvlc's log file, then dispose the player, then
        /// LibVLC. Disposing the output window before stopping the player used to hit
        /// set_hwnd(NULL) against a live player and could hang -- and this path runs on
        /// every vout change, which is exactly the moment the operator reaches for it
        /// because something already looks broken.
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
                try { _libVlc.CloseLogFile(); } catch { /* best effort during teardown */ }
                _libVlc.Dispose();
                _libVlc = null;
            }
        }

        private void OnFormClosingCleanup()
        {
            ShutdownVlc();

            lock (_logLock)
            {
                try { _logWriter?.Flush(); _logWriter?.Dispose(); }
                catch { /* best effort -- the process is exiting anyway */ }
                _logWriter = null;
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

            // Shown BEFORE Play(), not after: libvlc builds its D3D9/DirectDraw vout
            // against whatever window it's handed at that moment, and a window that's
            // parked/invisible then and only reparented/shown afterwards is untested
            // territory on a one-shot trip. Costs nothing here -- geometry is already
            // applied by CreateLibVlc, and SpikeForm being TopMost means this doesn't
            // lock the operator out of Apply/Play/Stop.
            if (_outputWindow != null && !_outputWindow.Visible)
            {
                _outputWindow.Show();
                Log("Output window shown.");
            }

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
        /// time anything works. Reading a Stopwatch is pure managed code (no libvlc
        /// call at all), so it's captured HERE, still on the callback thread, for an
        /// accurate GAP number -- if this were deferred to the UI thread instead, GAP
        /// would measure "click to Playing" PLUS "however long the UI thread was busy
        /// before it got around to pumping the posted message" (that thread does a
        /// synchronous file write per log line), which would quietly pollute the one
        /// number this whole exercise depends on. Everything that DOES touch libvlc
        /// (_player.Media, media.Tracks) still waits for the UI thread.
        /// </summary>
        private void OnLibVlcPlaying(object sender, EventArgs e)
        {
            long? gapMs = null;
            if (_gapStopwatch.IsRunning)
            {
                _gapStopwatch.Stop();
                gapMs = _gapStopwatch.ElapsedMilliseconds;
            }

            try
            {
                if (IsHandleCreated) BeginInvoke((Action)(() => OnPlayingOnUiThread(gapMs)));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnPlayingOnUiThread(long? gapMs)
        {
            Log($"MediaPlayer.Playing (slot {_currentSlot}).");

            if (gapMs.HasValue)
            {
                var fromLabel = _switchFromSlot ?? "START";
                Log($"GAP {fromLabel}->{_currentSlot}: {gapMs.Value} ms");
            }

            LogResolutionIfKnown();
        }

        /// <summary>
        /// Polls on a UI-thread timer (not a libvlc callback) until the player reports
        /// an active video output -- closer to "a frame is actually on the wall" than
        /// the Playing state-machine transition above, which can fire slightly before
        /// that. Note: System.Windows.Forms.Timer has the same ~15.6ms floor as
        /// DateTime.Now regardless of the 5ms Interval requested here, so this value is
        /// quantized to that granularity -- a coarse indicator, not a precise one. GAP
        /// above is unaffected: that's a genuine Stopwatch read taken directly in the
        /// libvlc callback, not on a timer tick. If no vout ever comes up, this logs an
        /// explicit timeout rather than staying silent -- "nothing was logged" would
        /// otherwise be ambiguous between "the timer never ran" and "no vout ever
        /// appeared", and the latter is precisely the interesting result.
        /// </summary>
        private void FirstPictureTimerTick(object sender, EventArgs e)
        {
            if (_player != null && _player.VoutCount > 0)
            {
                _firstPictureTimer.Stop();
                Log($"FIRST PICTURE: {_firstPictureStopwatch.ElapsedMilliseconds} ms (slot {_currentSlot})");
                return;
            }

            if (_firstPictureStopwatch.Elapsed > FirstPictureTimeout)
            {
                _firstPictureTimer.Stop();
                Log($"FIRST PICTURE: NOT REACHED after {(int)FirstPictureTimeout.TotalSeconds}s (slot {_currentSlot})");
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
