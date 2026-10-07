using System;

namespace SimpleWall.Infrastructure
{
    /// <summary>
    /// Whether a silent UI thread has been silent long enough to call it hung.
    ///
    /// This exists because of 2026-07-30. At 16:59:00.5 the UI thread deadlocked inside libvlc
    /// during a clip swap and never came back. The process stayed alive, libvlc's own decode and
    /// vout threads stayed alive, and the wall carried on looping the night clip perfectly --
    /// which is exactly why nobody could tell. What actually stopped was the message pump: 15
    /// scheduled cues came and went unfired, the Stream Deck and the mouse did nothing, and not
    /// one line reached the log, for three days.
    ///
    /// A wall that is visibly black gets fixed in ten minutes. A wall that looks perfect and
    /// silently ignores every instruction gets fixed on Monday. That is the failure this watches
    /// for, and it is the reason the answer has to be a restart rather than an alert.
    ///
    /// Pure and separate from <see cref="UiWatchdog"/> for the usual reason in this project: the
    /// one branch that decides to kill a running wall should be readable and provable on its own,
    /// not tangled up with a thread and a Process.Kill.
    /// </summary>
    public static class WatchdogPolicy
    {
        /// <summary>
        /// True when the UI thread has gone quiet for longer than we are willing to believe.
        ///
        /// The threshold itself lives in WallConfig.WatchdogSeconds, where the reasoning for its
        /// size is written down. What matters here is what canNOT trip this: a modal dialog runs
        /// its own message loop and still dispatches WM_TIMER, so the beat keeps coming while a
        /// file picker is open, a menu is held down or the window is being dragged. Thumbnail
        /// extraction runs on a Task.Run (see ThumbnailCache), not on this thread. A config save is
        /// an atomic file write measured in milliseconds. What stops the beat for minutes is a
        /// message pump that has stopped running, and that does not get better on its own.
        ///
        /// <paramref name="sinceLastBeat"/> MUST be measured monotonically (Stopwatch), never by
        /// subtracting DateTimes. This machine's clock is known to move: TickGuard exists because
        /// a flat CMOS battery boots it in 2019 and w32time then yanks it to today. A wall-clock
        /// correction landing between two checks would compute hours of "silence" out of a
        /// perfectly healthy wall and restart it. UiWatchdog uses Stopwatch ticks for this reason
        /// and that choice is load-bearing, not tidiness.
        ///
        /// A threshold of zero or less means the watchdog is switched off (WallConfig.
        /// WatchdogSeconds = 0). Handled here rather than at the call site so "disabled" is one of
        /// the cases the tests pin down, instead of an `if` somebody can drop.
        ///
        /// Strictly greater than, so a threshold that is exactly met is still healthy -- the
        /// boundary belongs on the side that does NOT kill a running wall.
        /// </summary>
        public static bool IsHung(TimeSpan sinceLastBeat, TimeSpan threshold) =>
            threshold > TimeSpan.Zero && sinceLastBeat > threshold;
    }
}
