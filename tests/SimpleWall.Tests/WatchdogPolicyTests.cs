using System;
using SimpleWall.Infrastructure;
using Xunit;

namespace SimpleWall.Tests
{
    public class WatchdogPolicyTests
    {
        private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(60);

        [Fact]
        public void AUiThreadThatBeatEverySecondIsHealthy()
        {
            Assert.False(WatchdogPolicy.IsHung(TimeSpan.FromSeconds(1), Threshold));
        }

        /// <summary>
        /// The false-positive side, and the one that costs real money: restarting a working wall
        /// mid-programme. A UI thread busy with a file dialog, a slow network share or an atomic
        /// config write is late, not dead, and must be left alone.
        /// </summary>
        [Fact]
        public void AMerelySlowUiThreadIsNotRestarted()
        {
            Assert.False(WatchdogPolicy.IsHung(TimeSpan.FromSeconds(10), Threshold));
            Assert.False(WatchdogPolicy.IsHung(TimeSpan.FromSeconds(59), Threshold));
        }

        /// <summary>The boundary belongs on the side that does not kill a running wall.</summary>
        [Fact]
        public void ExactlyAtTheThresholdIsStillHealthy()
        {
            Assert.False(WatchdogPolicy.IsHung(Threshold, Threshold));
        }

        /// <summary>
        /// The 2026-07-30 case. The pump stopped for three days: 15 scheduled cues unfired, no OSC,
        /// no mouse, not one log line -- while the wall carried on looping and looking perfect.
        /// </summary>
        [Fact]
        public void APumpThatHasStoppedIsHung()
        {
            Assert.True(WatchdogPolicy.IsHung(TimeSpan.FromSeconds(61), Threshold));
            Assert.True(WatchdogPolicy.IsHung(TimeSpan.FromDays(3), Threshold));
        }

        /// <summary>
        /// WallConfig.WatchdogSeconds = 0 is the switch for whoever is standing in front of a wall
        /// that this thing has just restarted for no reason. It must be a real off, not a small
        /// threshold: at zero, nothing is ever hung.
        /// </summary>
        [Fact]
        public void AZeroThresholdSwitchesTheWatchdogOff()
        {
            Assert.False(WatchdogPolicy.IsHung(TimeSpan.FromDays(3), TimeSpan.Zero));
        }

        /// <summary>A hand-edited negative in config.json is off, not "everything is hung".</summary>
        [Fact]
        public void ANegativeThresholdIsAlsoOff()
        {
            Assert.False(WatchdogPolicy.IsHung(TimeSpan.FromDays(3), TimeSpan.FromSeconds(-5)));
        }
    }
}
