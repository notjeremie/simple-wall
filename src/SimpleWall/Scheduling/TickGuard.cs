using System;

namespace SimpleWall.Scheduling
{
    /// <summary>
    /// Decides when the tick window is nonsense and should be thrown away rather than fired.
    ///
    /// The scheduler's no-catch-up rule is applied at startup by seeding previousTick to "now". This
    /// applies exactly the same principle to every OTHER way the window can go wrong, because a
    /// one-second timer only ever produces a one-second window when the clock is well behaved, and
    /// this clock is not:
    ///
    ///   * **The clock leaps forward.** A Win7 wall PC with a flat CMOS battery boots believing it
    ///     is 2019, seeds previousTick there, and w32time then corrects it to today. Without this,
    ///     the next tick walks ~2,750 calendar dates: every weekly task fires once, and every
    ///     one-off in seven years fires and is marked Spent. A burst of years-old commands hits the
    ///     wall and the schedule is permanently burned, unattended, at 4am.
    ///   * **The clock goes backwards.** DueBetween correctly returns nothing, but previousTick is
    ///     then stranded in the future and NOTHING fires until real time catches up -- an hour, or
    ///     a year, of silently dead schedule.
    ///
    /// Both are resynced instead: skip the window, log it, carry on from now. Skipping a task
    /// because the clock lied is a missed cue; firing 2,750 of them is a broken wall.
    /// </summary>
    public static class TickGuard
    {
        /// <summary>
        /// Generous next to a 1s timer. A UI thread blocked by a modal dialog or a slow disk still
        /// produces windows far inside this, and those tasks SHOULD fire, just late -- so this only
        /// ever catches a clock that actually moved.
        /// </summary>
        public static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(5);

        /// <summary>True when the window is not believable and should be skipped.</summary>
        public static bool ShouldResync(DateTime previousTick, DateTime now) =>
            now < previousTick || now - previousTick > MaxWindow;
    }
}
