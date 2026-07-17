using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimpleWall.Engine;

namespace SimpleWall.Scheduling
{
    /// <summary>
    /// One entry in the schedule: either a weekly recurrence (a set of
    /// <see cref="DayOfWeek"/> values plus a time-of-day) or a single one-off date. The
    /// <see cref="Scheduler"/> is what decides whether a task is actually due in a given
    /// tick window - this class only knows how to answer "does this task fire on this
    /// calendar date", via <see cref="FiresOn"/>.
    ///
    /// Not thread-safe, same as everything else on this path: the scheduler ticks on the
    /// UI thread (a WinForms timer, Task 13), so no locking is added here.
    /// </summary>
    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Weekdays this task recurs on. Ignored when <see cref="OneOffDate"/> is set - a
        /// task is either a weekly recurrence or a one-off, never both.
        /// </summary>
        public List<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();

        /// <summary>When set, this task fires once on this calendar date instead of recurring.</summary>
        public DateTime? OneOffDate { get; set; }

        public TimeSpan Time { get; set; }
        public WallCommand Command { get; set; }

        /// <summary>
        /// Set by the scheduler once a one-off task has fired, so it never fires again -
        /// including if the app restarts and re-reads the same date. Meaningless for
        /// recurring tasks, which have no notion of "used up".
        /// </summary>
        public bool Spent { get; set; }

        /// <summary>
        /// Whether this task is scheduled to fire on the given calendar date (time-of-day
        /// is not considered here - see <see cref="Scheduler.DueBetween"/> for how the
        /// exact moment is combined with this date and checked against the tick window).
        /// </summary>
        public bool FiresOn(DateTime date)
        {
            if (OneOffDate.HasValue) return OneOffDate.Value.Date == date.Date;
            return Days.Contains(date.DayOfWeek);
        }

        /// <summary>
        /// The task as a sentence: "Every Sun at 13:00 -> play clip 7 (intro.mp4)".
        ///
        /// A schedule is read far more often than it is written, usually by someone asking "why
        /// did that come up on the wall?" or "will this fire on Sunday?" -- so the list has to
        /// answer in a sentence, not in columns the reader has to decode.
        ///
        /// <paramref name="clipName"/> is a lookup rather than a ClipLibrary so this class stays
        /// free of the clip roster (and stays a plain unit test). It returns null for a slot with
        /// no clip, which is a real state worth showing rather than hiding.
        /// </summary>
        public string Describe(Func<int, string> clipName = null)
        {
            return $"{DescribeWhen()} at {Time:hh\\:mm} -> {DescribeCommand(clipName)}";
        }

        private string DescribeWhen()
        {
            if (OneOffDate.HasValue)
                return "On " + OneOffDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // A weekly task with no days ticked can never fire. Saying so is the whole point of a
            // description -- the alternative is an entry that looks scheduled and silently isn't.
            if (Days == null || Days.Count == 0) return "Never (no days chosen)";

            if (Days.Distinct().Count() >= 7) return "Every day";

            // DayOfWeek starts at Sunday, which is also where the week starts here.
            var names = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames;
            var days = Days.Distinct().OrderBy(d => (int)d).Select(d => names[(int)d]);
            return (Days.Distinct().Count() == 1 ? "Every " : "") + string.Join(", ", days);
        }

        private string DescribeCommand(Func<int, string> clipName)
        {
            if (Command == null) return "(no command)";

            switch (Command.Kind)
            {
                case CommandKind.PlayClip:
                    var name = clipName?.Invoke(Command.Slot);
                    return $"play clip {Command.Slot} ({name ?? "no clip in this slot"})";

                case CommandKind.Brightness:
                    return "brightness " + Command.Value.ToString("0.00", CultureInfo.InvariantCulture);

                case CommandKind.Contrast:
                    return "contrast " + Command.Value.ToString("0.00", CultureInfo.InvariantCulture);

                default:
                    return Command.Kind.ToString().ToLowerInvariant();
            }
        }
    }
}
