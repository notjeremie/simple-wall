using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;

namespace SimpleWall.Tests
{
    public class SchedulerTests
    {
        private static readonly DateTime SundayNoon = new DateTime(2026, 7, 19, 12, 0, 0); // a Sunday

        private static ScheduledTask WeeklyTask(DayOfWeek day, int hour, int minute, int slot) =>
            new ScheduledTask
            {
                Enabled = true,
                Days = new List<DayOfWeek> { day },
                Time = new TimeSpan(hour, minute, 0),
                Command = WallCommand.PlayClip(slot)
            };

        private static Scheduler SchedulerWith(params ScheduledTask[] tasks) =>
            new Scheduler(new List<ScheduledTask>(tasks)) { Enabled = true };

        [Fact]
        public void TaskFiresWhenItsTimeIsCrossed()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));

            var due = scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1));

            Assert.Equal(7, Assert.Single(due).Command.Slot);
        }

        [Fact]
        public void TaskDoesNotFireOnTheWrongWeekday()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Monday, 13, 0, 7));

            Assert.Empty(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void TaskFiresOnlyOncePerCrossing()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            var justBefore = SundayNoon.AddHours(1).AddSeconds(-1);
            var justAfter = SundayNoon.AddHours(1);

            scheduler.DueBetween(justBefore, justAfter);
            var second = scheduler.DueBetween(justAfter, justAfter.AddSeconds(1));

            Assert.Empty(second);
        }

        [Fact]
        public void MissedTaskDoesNotFireOnStartup()
        {
            // no catch-up: app starts at 13:20, the 13:00 task is gone
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            var startup = SundayNoon.AddHours(1).AddMinutes(20);

            Assert.Empty(scheduler.DueBetween(startup, startup.AddSeconds(1)));
        }

        [Fact]
        public void BackwardClockJumpDoesNotReplayATask()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            var after = SundayNoon.AddHours(1).AddMinutes(30);

            Assert.Empty(scheduler.DueBetween(after, after.AddHours(-1))); // clock went backwards
        }

        [Fact]
        public void DisabledTaskNeverFires()
        {
            var task = WeeklyTask(DayOfWeek.Sunday, 13, 0, 7);
            task.Enabled = false;

            Assert.Empty(SchedulerWith(task).DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void MasterDisableSuppressesEverything()
        {
            var scheduler = SchedulerWith(WeeklyTask(DayOfWeek.Sunday, 13, 0, 7));
            scheduler.Enabled = false;

            Assert.Empty(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void OneOffFiresOnItsDateOnly()
        {
            var task = new ScheduledTask
            {
                Enabled = true,
                OneOffDate = SundayNoon.Date,
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.PlayClip(9)
            };
            var scheduler = SchedulerWith(task);

            Assert.Single(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));

            var nextWeek = SundayNoon.AddDays(7).AddHours(1);
            Assert.Empty(scheduler.DueBetween(nextWeek.AddSeconds(-1), nextWeek));
        }

        [Fact]
        public void EveryDayTaskFiresOnAnyWeekday()
        {
            var task = new ScheduledTask
            {
                Enabled = true,
                Days = new List<DayOfWeek>((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek))),
                Time = new TimeSpan(9, 0, 0),
                Command = WallCommand.PlayClip(9)
            };
            var scheduler = SchedulerWith(task);

            var wednesday = new DateTime(2026, 7, 22, 9, 0, 0);
            Assert.Single(scheduler.DueBetween(wednesday.AddSeconds(-1), wednesday));
        }

        // --- Additional tests below, written to expose the date-arithmetic bug flagged in
        // the task brief: FiresOn(now) and "moment = now.Date + task.Time" both key off
        // NOW's date, which is wrong once the (previousTick, now] window straddles
        // midnight - the scheduled moment can legitimately belong to previousTick's date.

        [Fact]
        public void MidnightBoundary_TaskAtMidnightFiresWhenTickCrossesIntoThatDay()
        {
            // Given exactly as described in the brief: previousTick 23:59:59 the day
            // before, now 00:00:00 on the scheduled weekday.
            var task = WeeklyTask(DayOfWeek.Sunday, 0, 0, 3);
            var scheduler = SchedulerWith(task);

            var previousTick = new DateTime(2026, 7, 18, 23, 59, 59); // Saturday
            var now = new DateTime(2026, 7, 19, 0, 0, 0);             // Sunday

            Assert.Equal(3, Assert.Single(scheduler.DueBetween(previousTick, now)).Command.Slot);
        }

        [Fact]
        public void MidnightBoundary_TaskNearEndOfPreviousDayFiresWhenTickLandsJustAfterMidnight()
        {
            // This is the case that actually breaks the naive "now.Date + task.Time"
            // implementation: the task is scheduled for 23:59 on SATURDAY, but the tick
            // that should catch it doesn't land until just after midnight, when "now" is
            // already Sunday. If the code checks FiresOn(now) it looks at Sunday and the
            // task (which only fires on Saturday) is wrongly skipped, even though its
            // scheduled moment (Saturday 23:59:00) sits squarely inside the tick window.
            var task = WeeklyTask(DayOfWeek.Saturday, 23, 59, 5);
            var scheduler = SchedulerWith(task);

            var previousTick = new DateTime(2026, 7, 18, 23, 58, 0); // Saturday, before 23:59
            var now = new DateTime(2026, 7, 19, 0, 0, 2);            // Sunday, just after midnight

            Assert.Equal(5, Assert.Single(scheduler.DueBetween(previousTick, now)).Command.Slot);
        }

        [Fact]
        public void MultiHourWindowFiresA13OClockTaskExactlyOnce()
        {
            // Machine was busy / timer coalesced / resumed from a brief sleep: the tick
            // interval was much wider than usual, but this is NOT catch-up because the
            // moment falls inside the (previousTick, now] window, same day.
            var task = WeeklyTask(DayOfWeek.Wednesday, 13, 0, 4);
            var scheduler = SchedulerWith(task);

            var previousTick = new DateTime(2026, 7, 22, 12, 0, 0); // Wednesday
            var now = new DateTime(2026, 7, 22, 15, 0, 0);          // Wednesday, 3 hours later

            Assert.Equal(4, Assert.Single(scheduler.DueBetween(previousTick, now)).Command.Slot);
        }

        [Fact]
        public void TwoTasksDueInTheSameWindowBothFire()
        {
            var scheduler = SchedulerWith(
                WeeklyTask(DayOfWeek.Sunday, 13, 0, 1),
                WeeklyTask(DayOfWeek.Sunday, 13, 30, 2));

            var previousTick = SundayNoon; // 12:00
            var now = SundayNoon.AddHours(2); // 14:00, spans both 13:00 and 13:30

            var due = scheduler.DueBetween(previousTick, now);

            Assert.Equal(2, due.Count);
            Assert.Contains(due, t => t.Command.Slot == 1);
            Assert.Contains(due, t => t.Command.Slot == 2);
        }

        [Fact]
        public void SpentOneOffNeverFiresAgainEvenOnItsOwnDate()
        {
            var task = new ScheduledTask
            {
                Enabled = true,
                OneOffDate = SundayNoon.Date,
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.PlayClip(9),
                Spent = true
            };
            var scheduler = SchedulerWith(task);

            Assert.Empty(scheduler.DueBetween(SundayNoon.AddHours(1).AddSeconds(-1), SundayNoon.AddHours(1)));
        }

        [Fact]
        public void TasksRoundTripThroughConfigStore()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var store = new ConfigStore(path);
                var config = store.Load();
                config.Tasks.Add(new ScheduledTask
                {
                    Enabled = true,
                    Days = new List<DayOfWeek> { DayOfWeek.Sunday },
                    Time = new TimeSpan(13, 0, 0),
                    Command = WallCommand.PlayClip(7)
                });
                store.Save(config);

                var loaded = new ConfigStore(path).Load();

                var task = Assert.Single(loaded.Tasks);
                Assert.Equal(DayOfWeek.Sunday, Assert.Single(task.Days));
                Assert.Equal(new TimeSpan(13, 0, 0), task.Time);
                Assert.Equal(CommandKind.PlayClip, task.Command.Kind);
                Assert.Equal(7, task.Command.Slot);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        /// <summary>
        /// Spent is meaningless for a recurring task -- ScheduledTask.Spent says so in as many
        /// words -- but the scheduler used to honour it for any task. A weekly task carrying a
        /// stale Spent (converted from a fired one-off, or a hand-edited config) was killed
        /// forever: still ticked, still black, still a sensible sentence, and never firing.
        /// </summary>
        [Fact]
        public void ARecurringTaskIsNotKilledByAStaleSpentFlag()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Friday },
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.Simple(CommandKind.Stop),
                Spent = true // left over from a life as a one-off
            };
            var scheduler = new Scheduler(new List<ScheduledTask> { task });

            var friday = new DateTime(2026, 7, 17); // a Friday
            var due = scheduler.DueBetween(friday.AddHours(12).AddMinutes(59), friday.AddHours(13).AddMinutes(1));

            Assert.Single(due);
        }

        [Fact]
        public void ASpentOneOffStillNeverFiresAgain()
        {
            var task = new ScheduledTask
            {
                OneOffDate = new DateTime(2026, 7, 17),
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.Simple(CommandKind.Stop),
                Spent = true
            };
            var scheduler = new Scheduler(new List<ScheduledTask> { task });

            var day = new DateTime(2026, 7, 17);
            var due = scheduler.DueBetween(day.AddHours(12).AddMinutes(59), day.AddHours(13).AddMinutes(1));

            Assert.Empty(due);
        }

    }
}
