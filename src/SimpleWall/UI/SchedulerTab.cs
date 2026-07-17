using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;

namespace SimpleWall.UI
{
    /// <summary>
    /// The schedule: what the wall will do on its own, when nobody is in the room.
    ///
    /// Everything here is built in the constructor, nothing in Load -- see MainForm.
    /// </summary>
    public class SchedulerTab : UserControl
    {
        private readonly Scheduler _scheduler;
        private readonly ClipLibrary _library;
        private readonly Action _saveConfig;

        private readonly CheckBox _masterEnable;
        private readonly Label _disabledBanner;
        private readonly ListView _list;
        private readonly Button _edit;
        private readonly Button _remove;

        private bool _updating;

        public SchedulerTab(Scheduler scheduler, ClipLibrary library, Action saveConfig = null)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _saveConfig = saveConfig ?? (() => { });

            BackColor = Color.FromArgb(24, 24, 28);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.FromArgb(24, 24, 28),
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // master switch
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // banner
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // list
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

            _masterEnable = new CheckBox
            {
                Text = "Run the schedule",
                AutoSize = true,
                Checked = _scheduler.Enabled,
                ForeColor = Color.FromArgb(220, 220, 226),
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };
            _masterEnable.CheckedChanged += OnMasterEnableChanged;

            // Unmissable, not subtle: a silently disabled scheduler is a Sunday-afternoon
            // discovery, and by then whatever should have gone on the wall didn't.
            _disabledBanner = new Label
            {
                Text = "  THE SCHEDULE IS OFF -- nothing here will run.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(150, 40, 40),
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = Color.FromArgb(32, 32, 36),
                ForeColor = Color.FromArgb(220, 220, 226)
            };
            _list.Columns.Add("Task", -2);
            _list.ItemChecked += OnItemChecked;
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => EditSelected();

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            buttons.Controls.Add(NewButton("Add...", (s, e) => AddTask()));
            _edit = NewButton("Edit...", (s, e) => EditSelected());
            _remove = NewButton("Remove", (s, e) => RemoveSelected());
            buttons.Controls.Add(_edit);
            buttons.Controls.Add(_remove);

            root.Controls.Add(_masterEnable, 0, 0);
            root.Controls.Add(_disabledBanner, 0, 1);
            root.Controls.Add(_list, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            Refresh_();
        }

        private static Button NewButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(80, 26),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 226),
                BackColor = Color.FromArgb(48, 48, 54),
                Margin = new Padding(0, 0, 6, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
            button.Click += onClick;
            return button;
        }

        /// <summary>Rebuilds the list from the scheduler. Named with an underscore to avoid Control.Refresh.</summary>
        public void Refresh_()
        {
            _updating = true;
            try
            {
                var selectedId = SelectedTask()?.Id;

                _list.BeginUpdate();
                _list.Items.Clear();

                foreach (var task in _scheduler.Tasks)
                {
                    var item = new ListViewItem(task.Describe(NameOfClip))
                    {
                        Tag = task,
                        Checked = task.Enabled,
                        // Same convention as the clip grid: red means "this will not do what it says".
                        ForeColor = IsBroken(task) ? Color.FromArgb(220, 60, 60) : Color.FromArgb(220, 220, 226)
                    };
                    if (task.Id == selectedId) item.Selected = true;
                    _list.Items.Add(item);
                }

                _list.EndUpdate();
                _disabledBanner.Visible = !_scheduler.Enabled;
                _masterEnable.Checked = _scheduler.Enabled;
            }
            finally
            {
                _updating = false;
            }

            UpdateButtons();
        }

        /// <summary>
        /// A task that cannot do what its sentence says: it points at a slot with no clip, or at a
        /// clip whose file has gone. Shown red rather than silently failing at 13:00 on a Sunday.
        /// </summary>
        private bool IsBroken(ScheduledTask task)
        {
            if (task.Command == null) return true;
            if (task.Command.Kind != CommandKind.PlayClip) return false;

            var clip = _library.BySlot(task.Command.Slot);
            return clip == null || string.IsNullOrWhiteSpace(clip.Path) || !File.Exists(clip.Path);
        }

        private string NameOfClip(int slot)
        {
            var clip = _library.BySlot(slot);
            if (clip == null) return null;
            return string.IsNullOrEmpty(clip.Path) ? "(no file)" : Path.GetFileName(clip.Path);
        }

        private ScheduledTask SelectedTask() =>
            _list.SelectedItems.Count > 0 ? (ScheduledTask)_list.SelectedItems[0].Tag : null;

        private void UpdateButtons()
        {
            var hasSelection = _list.SelectedItems.Count > 0;
            _edit.Enabled = hasSelection;
            _remove.Enabled = hasSelection;
        }

        private void OnMasterEnableChanged(object sender, EventArgs e)
        {
            if (_updating) return;

            _scheduler.Enabled = _masterEnable.Checked;
            _disabledBanner.Visible = !_scheduler.Enabled;
            _saveConfig();
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_updating) return;

            ((ScheduledTask)e.Item.Tag).Enabled = e.Item.Checked;
            _saveConfig();
        }

        private void AddTask()
        {
            using (var dialog = new TaskEditDialog(null, _library))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _scheduler.Add(dialog.Task);
                _saveConfig();
                Refresh_();
            }
        }

        private void EditSelected()
        {
            var task = SelectedTask();
            if (task == null) return;

            using (var dialog = new TaskEditDialog(task, _library))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _saveConfig();
                Refresh_();
            }
        }

        private void RemoveSelected()
        {
            var task = SelectedTask();
            if (task == null) return;

            // Confirmed, because there is no undo and the sentence is the only record of what the
            // operator meant.
            var answer = MessageBox.Show(this,
                "Remove this task?\n\n" + task.Describe(NameOfClip),
                "SimpleWall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _scheduler.Remove(task);
            _saveConfig();
            Refresh_();
        }
    }
}
