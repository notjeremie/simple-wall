using System;
using System.Collections.Generic;
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
    }
}
