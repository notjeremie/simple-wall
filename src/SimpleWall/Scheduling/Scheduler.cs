using System;
using System.Collections.Generic;

namespace SimpleWall.Scheduling
{
    /// <summary>
    /// Decides which <see cref="ScheduledTask"/>s are due between two clock readings.
    /// The clock is a parameter, never read inside this class - <see cref="DueBetween"/>
    /// takes <c>previousTick</c>/<c>now</c> explicitly instead of calling
    /// <see cref="DateTime.Now"/> itself. That is deliberate: a scheduler bug surfaces once
    /// a week, at the worst possible time, on a machine nobody is watching, and cannot be
    /// reproduced on demand. Making the clock a parameter turns every weekday-matching,
    /// one-off-date, midnight-crossing and clock-jump scenario into a plain unit test that
    /// runs in milliseconds - nobody has to wait until Sunday to find out whether it works.
    ///
    /// No catch-up, by construction rather than by a special case: the caller (Task 13)
    /// sets <c>previousTick</c> to <see cref="DateTime.Now"/> at startup, so a task whose
    /// moment fell before that instant is simply never inside a (previousTick, now]
    /// window this method is asked about. Boot at 13:20 and a 13:00 task does not run -
    /// nothing unexpected ever appears on the wall while nobody is in the room to notice
    /// it went wrong.
    ///
    /// Not thread-safe: all access is expected to happen on the UI thread. The scheduler
    /// tick (Task 13) is a WinForms timer, same thread as the OSC listener's marshaled
    /// calls into <see cref="SimpleWall.Engine.IWallEngine"/>, so no locking is added here -
    /// if that assumption ever needs to change, it should be a deliberate design change,
    /// not a patch to this class.
    /// </summary>
    public class Scheduler
    {
        private readonly List<ScheduledTask> _tasks;

        public Scheduler(List<ScheduledTask> tasks) { _tasks = tasks; }

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Live view of the schedule. These are the same objects the list holds, and the list is
        /// the SAME instance a WallConfig owns, so edits here persist on its next Save.
        /// </summary>
        public IReadOnlyList<ScheduledTask> Tasks => _tasks;

        /// <summary>Adds a task. Null is ignored rather than stored to blow up at 13:00 on a Sunday.</summary>
        public void Add(ScheduledTask task)
        {
            if (task == null) return;
            _tasks.Add(task);
        }

        /// <summary>Removes by Id, so a caller holding a stale copy still removes the right one.</summary>
        public void Remove(ScheduledTask task)
        {
            if (task == null) return;
            _tasks.RemoveAll(t => t.Id == task.Id);
        }

        /// <summary>
        /// Tasks whose scheduled moment falls in (previousTick, now]. Half-open at the
        /// start so a task can never fire twice on successive ticks (a moment exactly
        /// equal to previousTick was already delivered by the previous call). Returns
        /// nothing if the clock went backwards - no replays.
        ///
        /// Each candidate calendar date between previousTick.Date and now.Date
        /// (inclusive) is checked in turn, not just now.Date: a window that straddles
        /// midnight can contain a moment that belongs to EITHER day. A task scheduled for
        /// Saturday 23:59 whose tick doesn't land until Sunday 00:00:02 still has its
        /// moment - Saturday 23:59:00 - inside that window, even though "now" has already
        /// rolled over to Sunday. Checking weekday-match and computing the moment purely
        /// from now.Date (as an earlier draft of this method did) would silently drop
        /// that task on every midnight-crossing tick; iterating the date range instead of
        /// trusting now.Date is what makes this correct.
        /// </summary>
        public List<ScheduledTask> DueBetween(DateTime previousTick, DateTime now)
        {
            var due = new List<ScheduledTask>();
            if (!Enabled || now <= previousTick) return due;

            foreach (var task in _tasks)
            {
                // Spent only means anything for a one-off -- see ScheduledTask.Spent, which says so
                // in as many words. Honouring it on a recurring task kills that task FOREVER, and
                // it is reachable: the scheduler sets Spent on a one-off, the operator later edits
                // it into a weekly, and the stale flag rides along. The row then looks perfectly
                // normal -- ticked, not red, a sensible sentence -- and never fires again, with
                // nothing logged. That is precisely the silent-lie failure this whole feature
                // exists to prevent.
                if (!task.Enabled) continue;
                if (task.Spent && task.OneOffDate.HasValue) continue;

                for (var date = previousTick.Date; date <= now.Date; date = date.AddDays(1))
                {
                    if (!task.FiresOn(date)) continue;

                    var moment = date + task.Time;
                    if (moment > previousTick && moment <= now)
                    {
                        if (task.OneOffDate.HasValue) task.Spent = true;
                        due.Add(task);
                        break; // a task fires at most once per DueBetween call
                    }
                }
            }
            return due;
        }
    }
}
