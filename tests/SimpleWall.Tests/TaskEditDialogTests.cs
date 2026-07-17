using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The dialog's initial state, asserted rather than eyeballed: a render shows ComboBoxes as
    /// blank whether they are genuinely empty or merely not painted by WM_PRINT, so the picture
    /// cannot answer this and these tests can.
    /// </summary>
    public class TaskEditDialogTests
    {
        private static ClipLibrary LibraryWithClips()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\clips\intro.mp4");
            library.Add(@"C:\clips\sunset.mp4");
            return library;
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var grandchild in Descendants(child)) yield return grandchild;
            }
        }

        [Fact]
        public void ANewTaskOpensWithACommandAndAClipAlreadyChosen()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                var combos = Descendants(dialog).OfType<ComboBox>().ToList();

                Assert.Equal(2, combos.Count); // command + clip
                Assert.All(combos, combo => Assert.NotNull(combo.SelectedItem));
            }
        }

        /// <summary>
        /// Real clips by name, never a bare slot number: scheduling clip 7 on a wall where slot 7
        /// is empty is exactly the Sunday-afternoon discovery this tab exists to prevent.
        /// </summary>
        [Fact]
        public void TheClipDropdownOffersTheRealClipsByName()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                var texts = Descendants(dialog).OfType<ComboBox>()
                    .SelectMany(c => c.Items.Cast<object>())
                    .Select(i => i.ToString())
                    .ToList();

                Assert.Contains(texts, t => t.Contains("intro.mp4") && t.StartsWith("1"));
                Assert.Contains(texts, t => t.Contains("sunset.mp4") && t.StartsWith("2"));
            }
        }

        [Fact]
        public void EditingAnExistingTaskLoadsItsValues()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Wednesday },
                Time = new TimeSpan(14, 30, 0),
                Command = WallCommand.PlayClip(2)
            };

            using (var dialog = new TaskEditDialog(task, LibraryWithClips()))
            {
                var checkedDays = Descendants(dialog).OfType<CheckBox>()
                    .Where(c => c.Checked).Select(c => (DayOfWeek)c.Tag).ToList();

                Assert.Equal(new[] { DayOfWeek.Wednesday }, checkedDays);

                var time = Descendants(dialog).OfType<DateTimePicker>()
                    .First(p => p.ShowUpDown);
                Assert.Equal(new TimeSpan(14, 30, 0), time.Value.TimeOfDay);
            }
        }

        /// <summary>
        /// What the dialog PRODUCES, which is its real contract -- not which controls are showing.
        /// A child of an unshown form reports Visible == false whatever it was set to, so the
        /// adaptation cannot be asserted that way (and the render can't show it either: composite
        /// controls don't paint via WM_PRINT). What matters anyway is the task that comes out.
        /// </summary>
        [Fact]
        public void ChoosingBrightnessProducesABrightnessCommandNotAClipOne()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                CommandCombo(dialog).SelectedItem = CommandKind.Brightness;
                Descendants(dialog).OfType<NumericUpDown>().Single().Value = 0.75m;
                TickAnyDay(dialog);

                Assert.Null(dialog.Apply());

                Assert.Equal(CommandKind.Brightness, dialog.Task.Command.Kind);
                Assert.Equal(0.75f, dialog.Task.Command.Value);
            }
        }

        [Fact]
        public void ChoosingAClipProducesItsSlotNotItsPosition()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                CommandCombo(dialog).SelectedItem = CommandKind.PlayClip;
                ClipCombo(dialog).SelectedIndex = 1; // "2 - sunset.mp4"
                TickAnyDay(dialog);

                Assert.Null(dialog.Apply());

                Assert.Equal(CommandKind.PlayClip, dialog.Task.Command.Kind);
                Assert.Equal(2, dialog.Task.Command.Slot);
            }
        }

        [Fact]
        public void TheChosenDaysEndUpOnTheTask()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                foreach (var box in Descendants(dialog).OfType<CheckBox>())
                    box.Checked = (DayOfWeek)box.Tag == DayOfWeek.Sunday || (DayOfWeek)box.Tag == DayOfWeek.Thursday;

                Assert.Null(dialog.Apply());

                Assert.Equal(new[] { DayOfWeek.Sunday, DayOfWeek.Thursday }, dialog.Task.Days.OrderBy(d => (int)d));
                Assert.Null(dialog.Task.OneOffDate);
            }
        }


        /// <summary>
        /// A weekly task with no days can never fire. Refusing it is the point: an entry that looks
        /// scheduled and silently isn't is the Sunday-afternoon discovery this tab exists to stop.
        /// </summary>
        [Fact]
        public void AWeeklyTaskWithNoDaysIsRefusedAndNothingIsWritten()
        {
            using (var dialog = new TaskEditDialog(null, LibraryWithClips()))
            {
                foreach (var box in Descendants(dialog).OfType<CheckBox>()) box.Checked = false;

                var problem = dialog.Apply();

                Assert.NotNull(problem);
                Assert.Contains("day", problem, StringComparison.OrdinalIgnoreCase);
                Assert.Null(dialog.Task.Command); // refused means nothing was written
            }
        }

        [Fact]
        public void SchedulingAClipWhenThereAreNoClipsIsRefused()
        {
            using (var dialog = new TaskEditDialog(null, new ClipLibrary()))
            {
                TickAnyDay(dialog);

                var problem = dialog.Apply();

                Assert.NotNull(problem);
                Assert.Contains("clip", problem, StringComparison.OrdinalIgnoreCase);
            }
        }


        /// <summary>
        /// A one-off that has fired carries Spent = true. Convert it to a weekly task and that flag
        /// used to ride along: the row looked completely normal -- ticked, not red, a sensible
        /// sentence -- and silently never fired again, forever. Any save is a fresh intention.
        /// </summary>
        [Fact]
        public void ConvertingAFiredOneOffToWeeklyLetsItFireAgain()
        {
            var fired = new ScheduledTask
            {
                OneOffDate = new DateTime(2026, 7, 10),
                Time = new TimeSpan(10, 0, 0),
                Command = WallCommand.PlayClip(1),
                Spent = true
            };

            using (var dialog = new TaskEditDialog(fired, LibraryWithClips()))
            {
                // Switch to weekly and tick a day, as the operator would.
                Descendants(dialog).OfType<RadioButton>().First(r => r.Text.Contains("week")).Checked = true;
                TickAnyDay(dialog);

                Assert.Null(dialog.Apply());

                Assert.False(dialog.Task.Spent, "a converted task must be allowed to fire again");
                Assert.Null(dialog.Task.OneOffDate);
            }
        }


        /// <summary>
        /// Opening a red "play clip 9 (no clip in this slot)" row must not quietly re-point it at
        /// clip 1. The operator double-clicks the red row to see WHY it is red; OK is the
        /// AcceptButton, so Enter would save the silent rewrite, the row would go green, and on
        /// Friday the wrong clip plays.
        /// </summary>
        [Fact]
        public void EditingATaskWhoseClipIsGoneDoesNotSilentlyRepointIt()
        {
            var orphan = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Friday },
                Time = new TimeSpan(18, 0, 0),
                Command = WallCommand.PlayClip(9) // no such slot
            };

            using (var dialog = new TaskEditDialog(orphan, LibraryWithClips()))
            {
                Assert.Null(ClipCombo(dialog).SelectedItem);

                var problem = dialog.Apply();

                Assert.NotNull(problem); // refused: the operator must choose
                Assert.Equal(9, orphan.Command.Slot); // and nothing was rewritten
            }
        }

        private static ComboBox ClipCombo(Control dialog) => Descendants(dialog).OfType<ComboBox>()
            .Single(c => c.Items.Count > 0 && c.Items[0].ToString().Contains("intro.mp4"));

        private static ComboBox CommandCombo(Control dialog) =>
            Descendants(dialog).OfType<ComboBox>().Single(c => c != ClipCombo(dialog));

        /// <summary>
        /// Apply() refuses a weekly task with no days -- correctly -- so every test that expects
        /// success ticks one first. The refusal itself is tested separately.
        /// </summary>
        private static void TickAnyDay(Control dialog) =>
            Descendants(dialog).OfType<CheckBox>().First().Checked = true;
    }
}
