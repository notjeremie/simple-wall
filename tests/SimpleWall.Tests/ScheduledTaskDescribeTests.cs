using System;
using System.Collections.Generic;
using SimpleWall.Engine;
using SimpleWall.Scheduling;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// A schedule is read far more often than written, usually by someone asking "why did that
    /// appear on the wall?" -- so these sentences are the feature, not decoration.
    /// </summary>
    public class ScheduledTaskDescribeTests
    {
        private static string NameOf(int slot) => slot == 7 ? "intro.mp4" : null;

        [Fact]
        public void AWeeklyTaskReadsAsASentence()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Sunday },
                Time = new TimeSpan(13, 0, 0),
                Command = WallCommand.PlayClip(7)
            };

            Assert.Equal("Every Sun at 13:00 -> play clip 7 (intro.mp4)", task.Describe(NameOf));
        }

        [Fact]
        public void AllSevenDaysReadsAsEveryDay()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek>((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek))),
                Time = new TimeSpan(8, 0, 0),
                Command = WallCommand.Simple(CommandKind.Stop)
            };

            Assert.Equal("Every day at 08:00 -> stop", task.Describe(NameOf));
        }

        [Fact]
        public void SeveralDaysAreListedInWeekOrderStartingSunday()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Sunday, DayOfWeek.Wednesday },
                Time = new TimeSpan(19, 30, 0),
                Command = WallCommand.WithValue(CommandKind.Brightness, 0.5f)
            };

            Assert.Equal("Sun, Wed, Fri at 19:30 -> brightness 0.50", task.Describe(NameOf));
        }

        [Fact]
        public void AOneOffReadsAsItsDate()
        {
            var task = new ScheduledTask
            {
                OneOffDate = new DateTime(2026, 8, 1),
                Time = new TimeSpan(20, 0, 0),
                Command = WallCommand.PlayClip(7)
            };

            Assert.Equal("On 2026-08-01 at 20:00 -> play clip 7 (intro.mp4)", task.Describe(NameOf));
        }

        /// <summary>
        /// A weekly task with no days ticked can never fire. An entry that looks scheduled and
        /// silently isn't is exactly the Sunday-afternoon discovery this UI exists to prevent.
        /// </summary>
        [Fact]
        public void AWeeklyTaskWithNoDaysSaysItWillNeverFire()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek>(),
                Time = new TimeSpan(9, 0, 0),
                Command = WallCommand.Simple(CommandKind.Play)
            };

            Assert.Equal("Never (no days chosen) at 09:00 -> play", task.Describe(NameOf));
        }

        /// <summary>A task pointing at an empty slot must say so, not name nothing.</summary>
        [Fact]
        public void ATaskPointingAtAnEmptySlotSaysSo()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Monday },
                Time = new TimeSpan(10, 0, 0),
                Command = WallCommand.PlayClip(42)
            };

            Assert.Equal("Every Mon at 10:00 -> play clip 42 (no clip in this slot)", task.Describe(NameOf));
        }

        [Fact]
        public void DescribeWithoutALookupStillWorks()
        {
            var task = new ScheduledTask
            {
                Days = new List<DayOfWeek> { DayOfWeek.Monday },
                Time = new TimeSpan(10, 0, 0),
                Command = WallCommand.PlayClip(7)
            };

            Assert.Contains("play clip 7", task.Describe());
        }
    }
}
