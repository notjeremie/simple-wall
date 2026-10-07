using System;
using System.Threading;
using SimpleWall.Infrastructure;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The thread, not the policy (WatchdogPolicyTests has that). These drive real time, so every
    /// threshold here is sub-second and every wait is generous: a watchdog test that goes red on a
    /// busy build agent teaches people to ignore it, which is the one outcome worse than not
    /// having it.
    ///
    /// onHung is injected throughout for the obvious reason -- the real one calls
    /// Process.GetCurrentProcess().Kill(), and the test runner is the current process.
    /// </summary>
    public class UiWatchdogTests
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

        /// <summary>
        /// A UI thread that never beats is exactly what 07-30 looked like from out here.
        /// </summary>
        [Fact]
        public void AUiThreadThatNeverBeatsIsCaught()
        {
            using (var fired = new ManualResetEventSlim(false))
            using (var watchdog = new UiWatchdog(TimeSpan.FromMilliseconds(200), null, () => fired.Set()))
            {
                watchdog.Start();
                Assert.True(fired.Wait(Generous), "a UI thread that never beat should have been called hung");
            }
        }

        /// <summary>
        /// The one that protects the wall from the watchdog. A beating UI thread must never be
        /// restarted, and this runs for many multiples of the threshold to say so.
        /// </summary>
        [Fact]
        public void ABeatingUiThreadIsNeverRestarted()
        {
            var fired = 0;
            using (var watchdog = new UiWatchdog(TimeSpan.FromMilliseconds(300), null, () => Interlocked.Increment(ref fired)))
            {
                watchdog.Start();

                var until = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < until)
                {
                    watchdog.Beat();
                    Thread.Sleep(25);
                }

                Assert.Equal(0, Volatile.Read(ref fired));
            }
        }

        /// <summary>
        /// A beat that STOPS is the real shape of the incident -- the pump ran for a week and then
        /// stopped mid-swap. Healthy first, then silence, then caught.
        /// </summary>
        [Fact]
        public void AUiThreadThatBeatsAndThenStopsIsCaught()
        {
            using (var fired = new ManualResetEventSlim(false))
            using (var watchdog = new UiWatchdog(TimeSpan.FromMilliseconds(200), null, () => fired.Set()))
            {
                watchdog.Start();

                for (var i = 0; i < 20; i++)
                {
                    watchdog.Beat();
                    Thread.Sleep(25);
                }
                Assert.False(fired.IsSet, "it was beating the whole time");

                Assert.True(fired.Wait(Generous), "the beat stopped and that should have been caught");
            }
        }

        /// <summary>
        /// Once. The real onHung schedules a relauncher and terminates; firing twice could only
        /// race two replacements onto the wall.
        /// </summary>
        [Fact]
        public void ItFiresOnlyOnce()
        {
            var fired = 0;
            using (var watchdog = new UiWatchdog(TimeSpan.FromMilliseconds(100), null, () => Interlocked.Increment(ref fired)))
            {
                watchdog.Start();
                Thread.Sleep(2000); // many check intervals past the threshold
                Assert.Equal(1, Volatile.Read(ref fired));
            }
        }

        /// <summary>
        /// Shutdown is not a hang. Program disposes the watchdog the instant Application.Run
        /// returns, because from then on the pump has legitimately stopped and so has the beat --
        /// and the config save still to come must not be killed halfway through.
        /// </summary>
        [Fact]
        public void ADisposedWatchdogStopsWatching()
        {
            var fired = 0;
            var watchdog = new UiWatchdog(TimeSpan.FromMilliseconds(100), null, () => Interlocked.Increment(ref fired));
            watchdog.Start();
            watchdog.Dispose();

            Thread.Sleep(1000); // ten thresholds of silence, all of it after teardown
            Assert.Equal(0, Volatile.Read(ref fired));
        }

        /// <summary>WallConfig.WatchdogSeconds = 0. Never starts, never fires.</summary>
        [Fact]
        public void AZeroThresholdNeverArms()
        {
            var fired = 0;
            using (var watchdog = new UiWatchdog(TimeSpan.Zero, null, () => Interlocked.Increment(ref fired)))
            {
                watchdog.Start();
                Thread.Sleep(500);
                Assert.Equal(0, Volatile.Read(ref fired));
            }
        }

        /// <summary>
        /// Disposing something that was never armed must not throw -- Program's `using` runs
        /// whether or not the config had the watchdog switched on.
        /// </summary>
        [Fact]
        public void DisposingAnUnstartedWatchdogIsSafe()
        {
            var watchdog = new UiWatchdog(TimeSpan.FromSeconds(60));
            watchdog.Dispose();
            watchdog.Dispose();
        }
    }
}
