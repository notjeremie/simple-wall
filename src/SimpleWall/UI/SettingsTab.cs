using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using SimpleWall.Infrastructure;
using SimpleWall.Model;

namespace SimpleWall.UI
{
    /// <summary>
    /// Setup: the things touched once when the machine is installed, and then never again.
    ///
    /// Everything here is built in the constructor, nothing in Load -- see MainForm.
    ///
    /// The theme of this tab is that it must not lie about the machine. Three specific ways it
    /// could, each guarded below:
    ///
    ///   1. **The OSC port box is a wish, not a fact.** The socket is bound at startup and cannot
    ///      be rebound. So the status line reports the port ACTUALLY bound, and says so loudly
    ///      when the box no longer agrees with it. A box reading 7001 over a status line reading
    ///      "listening on 7001" that was never true is how someone spends an afternoon on a
    ///      Stream Deck that was never going to work.
    ///   2. **Autostart can point somewhere else.** The Run value holds a path. Copy the app to a
    ///      new folder and the old value survives, so "autostart: on" means Windows will launch
    ///      SOMETHING, not this. See <see cref="Autostart.RegisteredPath"/>.
    ///   3. **The addresses come from the NIC, never from DNS.** Dns.GetHostAddresses on this
    ///      machine's own name is a network call: a bare hostname has already been measured in
    ///      this project at ~10 SECONDS, uncached, and this would run it on the UI thread while
    ///      building a window. NetworkInterface answers from the local stack.
    /// </summary>
    public class SettingsTab : UserControl
    {
        private readonly WallConfig _config;
        private readonly Action _applyGeometry;
        private readonly Action _saveConfig;
        private readonly Autostart _autostart;
        private readonly string _exePath;
        private readonly Action<string> _log;

        private readonly NumericUpDown _oscPort;
        private readonly TextBox _replyHost;
        private readonly NumericUpDown _replyPort;
        private readonly Label _oscStatus;
        private readonly TextBox _addresses;
        private readonly NumericUpDown _x, _y, _width, _height;
        private readonly CheckBox _autostartBox;
        private readonly Label _autostartStatus;
        private readonly Font _boldFont;

        private readonly Timer _debounce = new Timer { Interval = 1000 };

        /// <summary>
        /// What the OSC listener and reply sender were actually STARTED with. Snapshotted here
        /// rather than read back from the bound socket, because a configured port of 0 binds to an
        /// arbitrary free one -- comparing the box against the bound port would then shout
        /// "restart to apply" at an operator who has changed nothing.
        /// </summary>
        private readonly int _startedWithPort;
        private readonly string _startedWithReplyHost;
        private readonly int _startedWithReplyPort;

        private int _boundPort = -1;
        private string _oscFailure;
        private bool _geometryDirty;

        /// <summary>Set while the controls are being written FROM the config, so the change
        /// handlers don't write straight back and re-trigger themselves. Same pattern as
        /// SchedulerTab.</summary>
        private bool _updating;

        /// <param name="applyGeometry">
        /// VlcWallEngine.ApplyGeometry. An Action rather than a method on IWallEngine: geometry is
        /// not a command (it has no business in the WallCommand path that OSC and the scheduler
        /// share), and widening the interface would drag every stub and fixture along with it.
        /// </param>
        /// <param name="exePath">
        /// The EXE to register for autostart. Injectable only so a render fixture can show what
        /// this tab looks like when autostart points somewhere else -- production passes nothing
        /// and gets this process.
        /// </param>
        public SettingsTab(WallConfig config, Action applyGeometry = null, Action saveConfig = null,
            Autostart autostart = null, string exePath = null, Action<string> log = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _applyGeometry = applyGeometry ?? (() => { });
            _saveConfig = saveConfig ?? (() => { });
            _autostart = autostart ?? new Autostart();
            _exePath = exePath ?? Application.ExecutablePath;
            _log = log ?? (_ => { });

            BackColor = Color.FromArgb(24, 24, 28);
            _boldFont = new Font(Font, FontStyle.Bold);

            _oscPort = NewNumeric(0, 65535); _oscPort.Name = "oscPort";
            _replyHost = NewTextBox(); _replyHost.Name = "replyHost";
            _replyPort = NewNumeric(0, 65535); _replyPort.Name = "replyPort";
            _oscStatus = NewStatusLabel(); _oscStatus.Name = "oscStatus";
            _addresses = NewAddressBox(); _addresses.Name = "addresses";

            // X and Y go negative: a display arranged to the LEFT of the primary sits at a
            // negative origin, and that is an ordinary Windows setup rather than a mistake.
            _x = NewNumeric(-32768, 32767); _x.Name = "x";
            _y = NewNumeric(-32768, 32767); _y.Name = "y";

            // Zero is meaningful for width/height -- WallConfig documents it as "never configured"
            // and it routes to the wall via GeometryValidator.Resolve. The range allows it so the
            // box can show the truth; only Reset ever deliberately puts it there.
            _width = NewNumeric(0, 32767); _width.Name = "width";
            _height = NewNumeric(0, 32767); _height.Name = "height";

            _autostartBox = new CheckBox
            {
                Text = "Start SimpleWall when Windows starts",
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 226),
                Margin = new Padding(0, 2, 0, 2)
            };
            _autostartBox.Name = "autostart";
            _autostartStatus = NewStatusLabel(); _autostartStatus.Name = "autostartStatus";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                BackColor = Color.FromArgb(24, 24, 28),
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(root, Header("Remote control (OSC)"));
            AddRow(root, BuildOscGrid());
            AddRow(root, _oscStatus);
            AddRow(root, Header("This machine"));
            AddRow(root, Hint("Point the Stream Deck at one of these addresses and the port above."));
            AddRow(root, _addresses);
            AddRow(root, Header("Output window"));
            AddRow(root, BuildGeometryGrid());
            AddRow(root, BuildGeometryButtons());
            AddRow(root, Header("Startup"));
            AddRow(root, _autostartBox);
            AddRow(root, _autostartStatus);

            // Every row is AutoSize and every row holds exactly one control (see AddRow), so
            // leftover vertical space simply stays empty at the bottom and the content sits at the
            // top where it belongs -- no trailing percent row, and so nothing to float a control
            // into the middle of the tab. The Task 13 task-editor bug needed a control in a row
            // with NO RowStyle at all; there is none of that here.
            //
            // Counted from the rows actually added, never a literal. A hand-maintained RowCount is
            // wrong the moment a row is inserted, and TableLayoutPanel does not complain -- it
            // stacks the overflow into the last row, on top of whatever is already there.
            root.RowCount = root.RowStyles.Count;

            Controls.Add(root);

            SyncFromConfig();

            // The boxes clamp what they show (see SetNumeric), so after the first sync the config
            // could still hold a value the tab is NOT showing -- a hand-edited OscPort of 70000
            // reads back as 65535 in the box while 70000 sits in the config, and the next save
            // would then persist a number the operator never saw. Reconciling here, once, before
            // any handler is hooked, makes the config equal to what is on screen. It normalises an
            // out-of-range hand-edit rather than preserving it, which is the honest trade: the box
            // is the truth the operator can see. (Geometry is already resolved into range by the
            // engine at startup; this is belt-and-braces for it and the real fix for the port.)
            ReconcileConfigFromControls();

            // Snapshotted AFTER the reconcile, not before: the OSC sockets are created (in Program)
            // from the config as it stands now, i.e. the clamped values -- the listener reads
            // config.OscPort after this window is built. If the baseline were the pre-clamp value,
            // a hand-edited 70000 normalised to 65535 would read as a pending change and show a
            // spurious "restart to apply" for an edit the operator never made.
            _startedWithPort = _config.OscPort;
            _startedWithReplyHost = _config.OscReplyHost ?? "";
            _startedWithReplyPort = _config.OscReplyPort;

            RefreshOscStatus();
            RefreshAutostartStatus();

            // Hooked AFTER the initial sync, so writing the config's values into the controls
            // can't be mistaken for the operator typing them.
            _oscPort.ValueChanged += (s, e) => OnOscChanged();
            _replyHost.TextChanged += (s, e) => OnOscChanged();
            _replyPort.ValueChanged += (s, e) => OnOscChanged();
            _x.ValueChanged += (s, e) => OnGeometryChanged();
            _y.ValueChanged += (s, e) => OnGeometryChanged();
            _width.ValueChanged += (s, e) => OnGeometryChanged();
            _height.ValueChanged += (s, e) => OnGeometryChanged();
            _autostartBox.CheckedChanged += OnAutostartChanged;
            _debounce.Tick += (s, e) => { _debounce.Stop(); Commit(); };
        }

        // ----------------------------------------------------------------
        // OSC status: the only thing on this tab that knows what is true
        // ----------------------------------------------------------------

        /// <summary>
        /// Told by Program once the listener has actually tried to bind. Until then this tab has
        /// no idea whether OSC is running -- the window is built before the socket is opened,
        /// because the listener needs a window handle to marshal onto.
        /// </summary>
        /// <param name="boundPort">The port actually bound, or -1 if OSC is not running.</param>
        /// <param name="failure">Why it isn't running, or null.</param>
        public void SetOscStatus(int boundPort, string failure)
        {
            _boundPort = boundPort;
            _oscFailure = failure;
            RefreshOscStatus();
        }

        private void RefreshOscStatus()
        {
            if (_oscFailure != null)
            {
                Say(_oscStatus, _oscFailure, Bad);
                return;
            }

            if (_boundPort <= 0)
            {
                Say(_oscStatus, "OSC is not running. The wall works; the Stream Deck cannot reach it.", Bad);
                return;
            }

            if (OscSettingsChanged())
            {
                Say(_oscStatus, $"Listening on port {_boundPort}. Restart SimpleWall to apply the new OSC settings.", Warn);
                return;
            }

            Say(_oscStatus, $"Listening on port {_boundPort}.", Good);
        }

        /// <summary>
        /// Whether the boxes now say something other than what the sockets were started with.
        /// Ordinal, not culture-aware: a hostname is not prose.
        /// </summary>
        private bool OscSettingsChanged() =>
            _config.OscPort != _startedWithPort ||
            _config.OscReplyPort != _startedWithReplyPort ||
            !string.Equals(_config.OscReplyHost ?? "", _startedWithReplyHost, StringComparison.Ordinal);

        /// <summary>
        /// Re-reads the things that live outside this app and can change behind its back. Called
        /// when the tab is selected: autostart is a registry value, and msconfig or Task Manager's
        /// Startup tab can turn it off without telling anyone.
        /// </summary>
        public void RefreshFromSystem()
        {
            _updating = true;
            try { _autostartBox.Checked = _autostart.IsEnabled(); }
            catch { /* RefreshAutostartStatus reports it */ }
            finally { _updating = false; }

            RefreshAutostartStatus();
        }

        private void RefreshAutostartStatus()
        {
            try
            {
                if (!_autostart.IsEnabled())
                {
                    Say(_autostartStatus, "This machine will not start SimpleWall on its own.", Dim);
                    return;
                }

                if (!_autostart.PointsAt(_exePath))
                {
                    // The one case a tick box cannot express. Windows WILL launch something at
                    // logon, and it isn't this -- so the wall does not come back after a reboot
                    // and the box would have said it would.
                    Say(_autostartStatus,
                        "Autostart is on, but it points at a different copy:" + Environment.NewLine +
                        _autostart.RegisteredPath() + Environment.NewLine +
                        "Untick and re-tick to point it at this one.", Warn);
                    return;
                }

                Say(_autostartStatus, "Windows will start SimpleWall at logon.", Good);
            }
            catch (Exception ex)
            {
                // Reading HKCU should not fail, but this tab is the only thing that would ever
                // tell anyone it did.
                Say(_autostartStatus, "Could not read the autostart setting: " + ex.Message, Bad);
            }
        }

        // ----------------------------------------------------------------
        // Changes in
        // ----------------------------------------------------------------

        private void OnOscChanged()
        {
            if (_updating) return;

            _config.OscPort = (int)_oscPort.Value;
            _config.OscReplyHost = _replyHost.Text.Trim();
            _config.OscReplyPort = (int)_replyPort.Value;

            // Immediately, not on the debounce: this is the line that tells the operator their
            // edit has not taken effect yet, and it must appear as they type rather than a second
            // after they have stopped reading it.
            RefreshOscStatus();
            Bump();
        }

        private void OnGeometryChanged()
        {
            if (_updating) return;

            _config.OutputX = (int)_x.Value;
            _config.OutputY = (int)_y.Value;
            _config.OutputWidth = (int)_width.Value;
            _config.OutputHeight = (int)_height.Value;

            _geometryDirty = true;
            Bump();
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            if (_updating) return;

            try
            {
                _autostart.Set(_autostartBox.Checked, _exePath);
                _log($"Autostart {(_autostartBox.Checked ? "enabled for " + _exePath : "disabled")}");
            }
            catch (Exception ex)
            {
                // A checkbox that ticks and silently does nothing is how someone walks away
                // believing the wall will come back after the next reboot.
                _log("Autostart could not be changed: " + ex);
                Say(_autostartStatus, "Could not change autostart: " + ex.Message, Bad);

                // Put the box back where the registry actually is, rather than leaving it showing
                // the change that failed.
                _updating = true;
                try { _autostartBox.Checked = _autostart.IsEnabled(); }
                catch { /* already reported */ }
                finally { _updating = false; }
                return;
            }

            RefreshAutostartStatus();
        }

        /// <summary>
        /// Debounced, for the same reason the brightness slider is: ConfigStore.Save is an atomic
        /// file write, and TextChanged fires once per keystroke. Typing a hostname would otherwise
        /// be a dozen fsyncs.
        ///
        /// The geometry apply rides the same timer. A NumericUpDown has no "release" to hang it
        /// on -- typing 1964 passes through 1, 19 and 196 on the way, and each one is a real
        /// ValueChanged.
        /// </summary>
        private void Bump()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void Commit()
        {
            if (_geometryDirty)
            {
                _geometryDirty = false;
                ApplyGeometrySafely();
            }

            Save();
        }

        /// <summary>
        /// Zeros are NOT applied from here. GeometryValidator.Resolve reads a zero width or height
        /// as "never configured" and answers with the default strip on the wall -- which is
        /// exactly right for Reset, and exactly wrong for an operator who has cleared the box and
        /// is halfway through typing 1964. Only <see cref="ResetGeometry"/> goes there, on purpose.
        /// </summary>
        private void ApplyGeometrySafely()
        {
            if (_config.OutputWidth <= 0 || _config.OutputHeight <= 0) return;

            try
            {
                _applyGeometry();

                // ApplyGeometry resolves against the screens actually connected and writes the
                // result back into the config, so the boxes have to be re-read: asking for a
                // window on a monitor that is no longer there moves it, and the operator needs to
                // see where it actually went.
                SyncGeometry();
            }
            catch (Exception ex)
            {
                _log("Applying output geometry failed: " + ex);
            }
        }

        private void ResetGeometry()
        {
            // Zero means "never configured", which is what routes this to the LED wall rather than
            // to 0,0 on the operator's own desktop. See GeometryValidator.Resolve.
            _config.OutputWidth = 0;
            _config.OutputHeight = 0;
            _config.OutputX = 0;
            _config.OutputY = 0;

            _geometryDirty = false;
            _debounce.Stop();

            try
            {
                _applyGeometry();
            }
            catch (Exception ex)
            {
                _log("Resetting output geometry failed: " + ex);
            }

            SyncGeometry();
            Save();
        }

        private void Save()
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
        // Config in
        // ----------------------------------------------------------------

        private void SyncFromConfig()
        {
            _updating = true;
            try
            {
                SetNumeric(_oscPort, _config.OscPort);
                _replyHost.Text = _config.OscReplyHost ?? "";
                SetNumeric(_replyPort, _config.OscReplyPort);
                SyncGeometryCore();

                try { _autostartBox.Checked = _autostart.IsEnabled(); }
                catch { _autostartBox.Checked = false; /* RefreshAutostartStatus says so */ }
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>
        /// Writes the clamped control values back into the config, so it never holds a number the
        /// tab isn't showing. Deliberately mirrors what OnOscChanged and OnGeometryChanged persist,
        /// and touches nothing else -- autostart lives in the registry, not the config.
        /// </summary>
        private void ReconcileConfigFromControls()
        {
            _config.OscPort = (int)_oscPort.Value;
            _config.OscReplyHost = _replyHost.Text.Trim();
            _config.OscReplyPort = (int)_replyPort.Value;
            _config.OutputX = (int)_x.Value;
            _config.OutputY = (int)_y.Value;
            _config.OutputWidth = (int)_width.Value;
            _config.OutputHeight = (int)_height.Value;
        }

        private void SyncGeometry()
        {
            _updating = true;
            try { SyncGeometryCore(); }
            finally { _updating = false; }
        }

        private void SyncGeometryCore()
        {
            SetNumeric(_x, _config.OutputX);
            SetNumeric(_y, _config.OutputY);
            SetNumeric(_width, _config.OutputWidth);
            SetNumeric(_height, _config.OutputHeight);
        }

        /// <summary>
        /// Clamped, because NumericUpDown THROWS when handed a value outside its range, and
        /// config.json is deliberately not range-validated -- a hand-edited OutputX of 999999 would
        /// otherwise throw from this constructor, i.e. from MainForm's constructor, i.e. before
        /// Application.Run exists to catch it. The app would die with no window and no dialog. That
        /// is not a hypothetical: the exact shape of it shipped in Task 13 and a reviewer
        /// reproduced it as a live crash.
        /// </summary>
        private static void SetNumeric(NumericUpDown box, int value)
        {
            box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        // ----------------------------------------------------------------
        // Layout
        // ----------------------------------------------------------------

        private Control BuildOscGrid()
        {
            var grid = NewFieldGrid();
            AddField(grid, 0, "Listen port:", _oscPort);
            AddField(grid, 1, "Reply host:", _replyHost);
            AddField(grid, 2, "Reply port:", _replyPort);
            return grid;
        }

        private Control BuildGeometryGrid()
        {
            var grid = NewFieldGrid();
            AddField(grid, 0, "X:", _x);
            AddField(grid, 1, "Y:", _y);
            AddField(grid, 2, "Width:", _width);
            AddField(grid, 3, "Height:", _height);
            return grid;
        }

        private Control BuildGeometryButtons()
        {
            var flow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            flow.Controls.Add(NewButton("Reset output window", (s, e) => ResetGeometry()));
            flow.Controls.Add(Hint("Puts the window back on the LED wall at its default size."));
            return flow;
        }

        private TableLayoutPanel NewFieldGrid()
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(0, 2, 0, 6),
                BackColor = Color.FromArgb(24, 24, 28)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return grid;
        }

        private void AddField(TableLayoutPanel grid, int row, string caption, Control control)
        {
            grid.RowCount = Math.Max(grid.RowCount, row + 1);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 206),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            }, 0, row);
            grid.Controls.Add(control, 1, row);
        }

        private static void AddRow(TableLayoutPanel root, Control control)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(control, 0, root.RowStyles.Count - 1);
        }

        private Label Header(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = _boldFont,
            ForeColor = Color.FromArgb(220, 220, 226),
            Margin = new Padding(0, 10, 0, 4)
        };

        private static Label Hint(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 156),
            Margin = new Padding(0, 8, 0, 2)
        };

        private static Label NewStatusLabel() => new Label
        {
            AutoSize = true,
            Text = " ",
            Margin = new Padding(0, 2, 0, 2)
        };

        private static void Say(Label label, string text, Color color)
        {
            label.Text = text;
            label.ForeColor = color;
        }

        private static readonly Color Good = Color.FromArgb(120, 200, 130);
        private static readonly Color Warn = Color.FromArgb(230, 180, 90);
        private static readonly Color Bad = Color.FromArgb(230, 110, 110);
        private static readonly Color Dim = Color.FromArgb(150, 150, 156);

        private static NumericUpDown NewNumeric(int min, int max) => new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Width = 90,
            TextAlign = HorizontalAlignment.Right,
            BackColor = Color.FromArgb(40, 40, 46),
            ForeColor = Color.FromArgb(230, 230, 236),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 2)
        };

        private static TextBox NewTextBox() => new TextBox
        {
            Width = 220,
            BackColor = Color.FromArgb(40, 40, 46),
            ForeColor = Color.FromArgb(230, 230, 236),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 2)
        };

        /// <summary>
        /// The machine's own addresses, selectable, so setting up a Stream Deck means reading the
        /// screen instead of finding a keyboard and running ipconfig on a machine reachable only
        /// over VNC.
        ///
        /// ReadOnly rather than a Label: a Label cannot be selected, and an address that has to be
        /// copied by eye is an address that gets typed wrong once.
        /// </summary>
        private TextBox NewAddressBox()
        {
            var addresses = LocalAddresses();
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Width = 320,
                Height = Math.Max(22, addresses.Length * 18 + 6),
                Text = addresses.Length > 0
                    ? string.Join(Environment.NewLine, addresses)
                    : "No network connection -- the Stream Deck cannot reach this machine.",
                BackColor = Color.FromArgb(40, 40, 46),
                ForeColor = addresses.Length > 0 ? Color.FromArgb(230, 230, 236) : Bad,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 0, 2)
            };
            return box;
        }

        /// <summary>
        /// From the local network stack, never from DNS -- see rule 3 in the class docs.
        ///
        /// IPv4 only, and no loopback: this list exists to be typed into a Stream Deck on another
        /// machine, and neither ::1 nor a link-local IPv6 address is something anyone can use for
        /// that. Down interfaces are excluded for the same reason -- an address that cannot be
        /// reached is worse than no address, because someone will try it.
        /// </summary>
        private static string[] LocalAddresses()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses.Cast<UnicastIPAddressInformation>())
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
            catch
            {
                // Enumerating adapters is not worth a dead window.
                return new string[0];
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debounce.Dispose();
                _boldFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
