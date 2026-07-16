using System;
using System.Diagnostics;
using System.Threading;
using LibVLCSharp.Shared;
using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// These test libvlc, not us -- deliberately. VlcWallEngine's design rests on two facts
    /// about libvlc that are surprising, undocumented in any obvious place, and were each one
    /// review away from shipping as a disaster:
    ///
    ///   1. libvlc treats an unknown construction option as FATAL and returns NULL, so a
    ///      stale option string means the app opens a window and does nothing, forever.
    ///   2. :input-repeat is a countdown, not "forever".
    ///
    /// Both are load-bearing, neither is enforced by the compiler, and both would fail
    /// silently on a machine nobody watches. So they get pinned here, and a libvlc upgrade
    /// that changes either one fails the build instead of the wall.
    ///
    /// These run headless: --vout=dummy and vlc://pause, so no video card is involved.
    /// </summary>
    public class LibVlcContractTests
    {
        public LibVlcContractTests() => Core.Initialize();

        /// <summary>
        /// The one that matters most. The spike specified VLC 2.x logging options
        /// (--file-logging, --logfile=, --logmode=text) which do not exist in 3.x; libvlc_new
        /// returned NULL and the app would have been a dead window on the wall. If someone
        /// adds a bad option to VlcOptions.LibVlc(), this fails here instead of there.
        /// </summary>
        [Fact]
        public void ProductionLibVlcOptionsAreAllAccepted()
        {
            using (var vlc = new LibVLC(VlcOptions.LibVlc()))
            {
                Assert.False(string.IsNullOrEmpty(vlc.Version));
            }
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(3, 4)]
        public void InputRepeatIsACountdownNotForever(int repeat, int expectedPlays)
        {
            // vlc://pause:1 is one second of nothing -- no decoder, no vout, no file needed,
            // but a real, measurable duration. If :input-repeat meant "forever", every one of
            // these would time out instead of ending after (repeat + 1) plays.
            var elapsed = TimeToEndReached($":input-repeat={repeat}");

            Assert.True(elapsed.HasValue,
                $":input-repeat={repeat} never raised EndReached -- if it now means 'forever', " +
                "VlcWallEngine's restart safety net has nothing to hang on and the wall dies " +
                "silently after ~22 days.");

            // Each play is ~1s. Generous bounds: this is a timing test on an emulated VM, and
            // the point is to tell "N+1 plays" apart from "1 play" or "forever", not to
            // measure libvlc's clock.
            var seconds = elapsed.Value.TotalSeconds;
            Assert.True(seconds > expectedPlays - 0.5 && seconds < expectedPlays + 3,
                $":input-repeat={repeat} should play {expectedPlays}x (~{expectedPlays}s) but took {seconds:0.00}s");
        }

        private static TimeSpan? TimeToEndReached(string mediaOption)
        {
            using (var vlc = new LibVLC("--no-audio", "--vout=dummy", "--verbose=0"))
            using (var player = new MediaPlayer(vlc))
            using (var ended = new ManualResetEventSlim(false))
            {
                var stopwatch = new Stopwatch();
                player.EndReached += (s, e) => { stopwatch.Stop(); ended.Set(); };

                using (var media = new Media(vlc, "vlc://pause:1", FromType.FromLocation))
                {
                    media.AddOption(mediaOption);
                    stopwatch.Start();
                    player.Play(media);

                    var fired = ended.Wait(TimeSpan.FromSeconds(20));
                    player.Stop();
                    return fired ? stopwatch.Elapsed : (TimeSpan?)null;
                }
            }
        }
    }
}
