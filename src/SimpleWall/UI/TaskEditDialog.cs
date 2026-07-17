using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;

namespace SimpleWall.UI
{
    /// <summary>
    /// Add or edit one scheduled task.
    ///
    /// The editor adapts to the command: "play clip" offers the real clips by name, brightness and
    /// contrast offer a value. Asking for a slot number in the abstract is how someone schedules
    /// clip 7 on a wall where 7 is empty.
    ///
    /// Built entirely in the constructor, nothing in Load -- see MainForm for why.
    /// </summary>
    public class TaskEditDialog : Form
    {
        private static readonly TimeSpan DefaultTime = new TimeSpan(9, 0, 0);

        private readonly ClipLibrary _library;

        private readonly DateTimePicker _time;
        private readonly RadioButton _weekly;
        private readonly RadioButton _oneOff;
        private readonly CheckBox[] _dayBoxes;
        private readonly DateTimePicker _date;
        private readonly ComboBox _command;
        private readonly ComboBox _clip;
        private readonly NumericUpDown _value;
        private readonly Label _clipLabel;
        private readonly Label _valueLabel;

        /// <summary>The edited task. Only meaningful once ShowDialog returns OK.</summary>
        public ScheduledTask Task { get; }

        public TaskEditDialog(ScheduledTask task, ClipLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            Task = task ?? new ScheduledTask { Time = DefaultTime, Days = new List<DayOfWeek>() };

            Text = task == null ? "New scheduled task" : "Edit scheduled task";
            ClientSize = new Size(460, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // --- when
            _time = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Width = 90
            };
            root.Controls.Add(NewLabel("Time:"), 0, 0);
            root.Controls.Add(_time, 1, 0);

            _weekly = new RadioButton { Text = "Every week on:", AutoSize = true, Checked = true };
            _oneOff = new RadioButton { Text = "Once on:", AutoSize = true };
            _weekly.CheckedChanged += (s, e) => UpdateEnabledFields();

            var days = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(20, 0, 0, 0) };
            _dayBoxes = DaysOfWeek().Select(day => new CheckBox
            {
                Text = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames[(int)day],
                Tag = day,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0)
            }).ToArray();
            foreach (var box in _dayBoxes) days.Controls.Add(box);

            _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Margin = new Padding(20, 0, 0, 0) };

            root.Controls.Add(_weekly, 0, 1);
            root.Controls.Add(days, 1, 1);
            root.Controls.Add(_oneOff, 0, 2);
            root.Controls.Add(_date, 1, 2);

            // --- what
            _command = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _command.Items.AddRange(new object[]
            {
                CommandKind.PlayClip, CommandKind.Play, CommandKind.Pause,
                CommandKind.Toggle, CommandKind.Stop, CommandKind.Brightness, CommandKind.Contrast
            });
            _command.SelectedIndexChanged += (s, e) => UpdateEnabledFields();
            root.Controls.Add(NewLabel("Command:"), 0, 3);
            root.Controls.Add(_command, 1, 3);

            // Real clips by name, never a bare slot number: scheduling clip 7 on a wall where slot
            // 7 is empty is a Sunday-afternoon discovery, and the dropdown makes it impossible.
            _clip = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            _clipLabel = NewLabel("Clip:");
            root.Controls.Add(_clipLabel, 0, 4);
            root.Controls.Add(_clip, 1, 4);

            _value = new NumericUpDown
            {
                Minimum = (decimal)AdjustValue.Min,
                Maximum = (decimal)AdjustValue.Max,
                DecimalPlaces = 2,
                Increment = 0.05m,
                Value = (decimal)AdjustValue.Neutral,
                Width = 80
            };
            _valueLabel = NewLabel("Value:");
            root.Controls.Add(_valueLabel, 0, 5);
            root.Controls.Add(_value, 1, 5);

            Controls.Add(root);
            Controls.Add(BuildButtons());

            PopulateClips();
            LoadFrom(Task);
            UpdateEnabledFields();
        }

        private Control BuildButtons()
        {
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
            ok.Click += OnOk;

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(10)
            };
            panel.Controls.Add(cancel);
            panel.Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
            return panel;
        }

        private static Label NewLabel(string text) =>
            new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };

        private static IEnumerable<DayOfWeek> DaysOfWeek() =>
            ((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek))).OrderBy(d => (int)d);

        private void PopulateClips()
        {
            _clip.Items.Clear();
            foreach (var clip in _library.Clips)
                _clip.Items.Add(new ClipChoice(clip));
        }

        private void LoadFrom(ScheduledTask task)
        {
            // The picker needs a full date; only the time part is ever read back out.
            _time.Value = DateTime.Today.Add(task.Time == default(TimeSpan) ? DefaultTime : task.Time);

            _weekly.Checked = !task.OneOffDate.HasValue;
            _oneOff.Checked = task.OneOffDate.HasValue;
            _date.Value = task.OneOffDate ?? DateTime.Today;

            foreach (var box in _dayBoxes)
                box.Checked = task.Days != null && task.Days.Contains((DayOfWeek)box.Tag);

            _command.SelectedItem = task.Command?.Kind ?? CommandKind.PlayClip;

            if (task.Command != null && task.Command.Kind == CommandKind.PlayClip)
                _clip.SelectedItem = _clip.Items.Cast<ClipChoice>()
                    .FirstOrDefault(c => c.Slot == task.Command.Slot);
            if (_clip.SelectedItem == null && _clip.Items.Count > 0) _clip.SelectedIndex = 0;

            if (task.Command != null &&
                (task.Command.Kind == CommandKind.Brightness || task.Command.Kind == CommandKind.Contrast))
                _value.Value = (decimal)AdjustValue.Clamp(task.Command.Value);
        }

        private void UpdateEnabledFields()
        {
            foreach (var box in _dayBoxes) box.Enabled = _weekly.Checked;
            _date.Enabled = _oneOff.Checked;

            var kind = (CommandKind)(_command.SelectedItem ?? CommandKind.PlayClip);
            var isClip = kind == CommandKind.PlayClip;
            var isValue = kind == CommandKind.Brightness || kind == CommandKind.Contrast;

            _clip.Visible = _clipLabel.Visible = isClip;
            _value.Visible = _valueLabel.Visible = isValue;
        }

        /// <summary>
        /// Validates the fields and, if they are sound, writes them onto <see cref="Task"/>.
        /// Returns the problem to show the operator, or null on success. Mutates nothing when it
        /// returns a problem.
        ///
        /// Separate from the OK handler on purpose. It keeps the decision away from the MessageBox,
        /// which makes both halves testable: a headless test can call this and read the answer,
        /// whereas clicking OK cannot even be simulated (Button.PerformClick is a no-op on a form
        /// that was never shown) and the MessageBox would hang the test forever.
        /// </summary>
        public string Apply()
        {
            var kind = (CommandKind)(_command.SelectedItem ?? CommandKind.PlayClip);

            // Refused rather than saved: a weekly task with no days never fires, and an entry that
            // looks scheduled and silently isn't is the exact Sunday-afternoon discovery this whole
            // tab exists to prevent.
            if (_weekly.Checked && !_dayBoxes.Any(b => b.Checked))
                return "Pick at least one day, or this task will never fire.";

            if (kind == CommandKind.PlayClip && !(_clip.SelectedItem is ClipChoice))
                return "There are no clips to schedule yet. Add one on the Clips tab first.";

            Task.Time = _time.Value.TimeOfDay;
            Task.OneOffDate = _oneOff.Checked ? _date.Value.Date : (DateTime?)null;
            Task.Days = _weekly.Checked
                ? _dayBoxes.Where(b => b.Checked).Select(b => (DayOfWeek)b.Tag).ToList()
                : new List<DayOfWeek>();

            // Any save from this editor is a fresh intention, so it gets to fire again --
            // unconditionally, not just for one-offs. Clearing it only in one-off mode left a fired
            // one-off that was later converted to weekly carrying Spent = true forever: a row that
            // looks completely normal and silently never fires. (The scheduler now also ignores
            // Spent on recurring tasks, which is what the field's own docs always said. Belt and
            // braces: this flag has exactly one honest meaning and two places rely on it.)
            Task.Spent = false;

            Task.Command = BuildCommand(kind);
            return null;
        }

        private void OnOk(object sender, EventArgs e)
        {
            var problem = Apply();
            if (problem == null) return;

            // Keeps the dialog open: DialogResult is what closes it, so clearing it cancels the OK.
            DialogResult = DialogResult.None;
            MessageBox.Show(this, problem, "SimpleWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private WallCommand BuildCommand(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.PlayClip:
                    return WallCommand.PlayClip(((ClipChoice)_clip.SelectedItem).Slot);
                case CommandKind.Brightness:
                case CommandKind.Contrast:
                    return WallCommand.WithValue(kind, AdjustValue.Clamp((float)_value.Value));
                default:
                    return WallCommand.Simple(kind);
            }
        }

        /// <summary>A clip in the dropdown, shown the way the grid shows it: "7 - intro.mp4".</summary>
        private class ClipChoice
        {
            private readonly string _text;

            public ClipChoice(ClipEntry clip)
            {
                Slot = clip.Slot;
                var name = string.IsNullOrEmpty(clip.Path) ? "(no file)" : System.IO.Path.GetFileName(clip.Path);
                _text = $"{clip.Slot} - {name}";
            }

            public int Slot { get; }

            public override string ToString() => _text;
        }
    }
}
