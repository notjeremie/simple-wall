using System;
using SimpleWall.Scheduling;
using Xunit;

namespace SimpleWall.Tests
{
    public class TickGuardTests
    {
        private static readonly DateTime Now = new DateTime(2026, 7, 17, 13, 0, 0);

        [Fact]
        public void AnOrdinaryOneSecondWindowIsFine()
        {
            Assert.False(TickGuard.ShouldResync(Now.AddSeconds(-1), Now));
        }

        /// <summary>
        /// A UI thread blocked by a modal dialog or a slow disk still makes a believable window,
        /// and those tasks SHOULD fire -- late, but fire. The guard must not eat them.
        /// </summary>
        [Fact]
        public void ABlockedUiThreadStillFiresItsTasks()
        {
            Assert.False(TickGuard.ShouldResync(Now.AddSeconds(-30), Now));
            Assert.False(TickGuard.ShouldResync(Now.AddMinutes(-4), Now));
        }

        /// <summary>
        /// The one that matters. A Win7 box with a flat CMOS battery boots believing it is 2019 and
        /// w32time then corrects it. Without the guard the next tick walks ~2,750 dates: every
        /// weekly task fires, and every one-off in seven years fires and is burned.
        /// </summary>
        [Fact]
        public void AClockLeapingForwardYearsIsSkippedNotFired()
        {
            Assert.True(TickGuard.ShouldResync(new DateTime(2019, 1, 1), Now));
        }

        [Fact]
        public void AClockGoingBackwardsIsResyncedRatherThanStrandingTheSchedule()
        {
            // DueBetween correctly fires nothing, but previousTick would sit in the future and
            // NOTHING would fire until real time caught up -- an hour, or a year, of dead schedule.
            Assert.True(TickGuard.ShouldResync(Now.AddHours(1), Now));
        }
    }
}
