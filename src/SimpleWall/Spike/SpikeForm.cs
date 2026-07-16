using System;
using System.Drawing;
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
    /// Everything logs to spike-log.txt next to the EXE, because the only way
    /// evidence gets off the wall PC is as a file carried back over VNC.
    ///
    /// Throwaway: most of this is deleted in Task 9 once the real VlcWallEngine exists.
    /// </summary>
    public class SpikeForm : Form
    {
        private static readonly string[] VoutOptions = { "default", "direct3d9", "directdraw" };

        private readonly string _logPath;
        private readonly object _logLock = new object();

        private LibVLC _libVlc;
        private MediaPlayer _player;
        private OutputWindow _outputWindow;
        private string _currentSlot; // "A", "B", or null

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
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spike-log.txt");

            Text = "SimpleWall Spike -- VLC on Win7 probe";
            Width = 920;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(700, 500);

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
                Log($"vout changed to '{_voutComboBox.SelectedItem}' -- recreating LibVLC instance.");
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
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";

            lock (_logLock)
            {
                try { File.AppendAllText(_logPath, line + Environment.NewLine); }
                catch { /* the log pane is the fallback if the file write itself fails */ }
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
                var libVlcOptions = vout != "default"
                    ? new[] { "--vout=" + vout }
                    : new string[0];

                _libVlc = new LibVLC(libVlcOptions);
                _libVlc.Log += OnLibVlcLog;

                Log($"LibVLC created. Version: {_libVlc.Version}");
                Log($"vout option: {(libVlcOptions.Length > 0 ? libVlcOptions[0] : "(default)")}");
                Log($"Software decode forced: {_softwareDecodeCheckBox.Checked}");

                _player = new MediaPlayer(_libVlc);
                _player.Playing += (s, e) => { Log($"MediaPlayer.Playing (slot {_currentSlot})."); LogResolutionIfKnown(); };
                _player.Stopped += (s, e) => Log($"MediaPlayer.Stopped (slot {_currentSlot}).");
                _player.EncounteredError += (s, e) => Log($"MediaPlayer.EncounteredError (slot {_currentSlot}).");

                _outputWindow = new OutputWindow(_player);
                _outputWindow.Show();
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

        private void OnLibVlcLog(object sender, LogEventArgs e)
        {
            if (e.Level == LogLevel.Warning || e.Level == LogLevel.Error)
            {
                Log($"[libvlc {e.Level}] {e.Module}: {e.Message}");
            }
        }

        private void ShutdownVlc()
        {
            if (_outputWindow != null)
            {
                try { _outputWindow.Close(); } catch { }
                _outputWindow.Dispose();
                _outputWindow = null;
            }

            if (_player != null)
            {
                try { _player.Stop(); } catch { }
                _player.Dispose();
                _player = null;
            }

            if (_libVlc != null)
            {
                _libVlc.Log -= OnLibVlcLog;
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

            _currentSlot = slot;
            _player.Play(media);
            Log($"MediaPlayer.Play() called for slot {slot}.");

            _player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            ApplyBrightness();
            ApplyContrast();
        }

        private void StopPlayback()
        {
            Log("--- Stop requested ---");
            _player?.Stop();
            _currentSlot = null;
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
            _brightnessValueLabel.Text = value.ToString("0.00");
            if (_player == null) return;
            _player.SetAdjustFloat(VideoAdjustOption.Brightness, value);
            Log($"Brightness set to {value:0.00}");
        }

        private void ApplyContrast()
        {
            var value = _contrastTrackBar.Value / 100f;
            _contrastValueLabel.Text = value.ToString("0.00");
            if (_player == null) return;
            _player.SetAdjustFloat(VideoAdjustOption.Contrast, value);
            Log($"Contrast set to {value:0.00}");
        }
    }
}
