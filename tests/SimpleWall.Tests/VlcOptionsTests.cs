using System;
using System.Linq;
using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    public class VlcOptionsTests
    {
        /// <summary>
        /// These three killed the app once already: they are VLC 2.x options, libvlc 3.x
        /// treats an unknown option as fatal, and libvlc_new() returns NULL. LibVlcContractTests
        /// proves the real list constructs; this says out loud which strings must never come
        /// back, so the next person to want a log file reaches for SetLogFile instead.
        /// </summary>
        [Theory]
        [InlineData("--file-logging")]
        [InlineData("--logfile")]
        [InlineData("--logmode")]
        public void NoVlc2LoggingOptionsSurvive(string banned)
        {
            Assert.DoesNotContain(VlcOptions.LibVlc(), o => o.StartsWith(banned, StringComparison.Ordinal));
        }

        [Fact]
        public void AudioIsOffAtBothLevels()
        {
            // The clips carry AAC and VLC was routing it to the sound card. This is a video wall.
            Assert.Contains("--no-audio", VlcOptions.LibVlc());
            Assert.Contains(":no-audio", VlcOptions.Media());
        }

        [Fact]
        public void MediaLoopsAtTheMaximumRepeat()
        {
            Assert.Contains($":input-repeat={VlcOptions.InputRepeat}", VlcOptions.Media());
        }

        /// <summary>
        /// Guards the arithmetic behind the 22-day claim rather than the constant itself.
        /// Nobody is going to sit through the real thing, so the mechanism is proven in
        /// LibVlcContractTests (plays == repeat + 1) and the consequence is checked here: at
        /// our repeat count a short clip still runs out well inside this deployment's life,
        /// which is why VlcWallEngine's EndReached restart is reachable code and not paranoia.
        /// </summary>
        [Fact]
        public void MaximumInputRepeatStillExpiresWithinMonths()
        {
            const double clipSeconds = 30.0;
            var lifetime = TimeSpan.FromSeconds((VlcOptions.InputRepeat + 1) * clipSeconds);

            Assert.True(lifetime < TimeSpan.FromDays(30),
                "A 30s clip at this :input-repeat outlasts a month, so the EndReached restart " +
                "would be unreachable -- re-check why it is here before deleting it.");
        }

        [Fact]
        public void DecoderIsNamedUpFrontToSkipTheFailedD3D11Attempt()
        {
            Assert.Contains(":avcodec-hw=dxva2", VlcOptions.Media());
        }

        /// <summary>
        /// The spike's Win7 insurance (forced software decode, --vout=direct3d9/directdraw)
        /// was proven unnecessary on the real machine and deliberately dropped. Re-adding it
        /// on a hunch is how the default path that actually works gets broken.
        /// </summary>
        [Fact]
        public void NoWin7FallbacksCameBack()
        {
            var all = VlcOptions.LibVlc().Concat(VlcOptions.Media()).ToArray();
            Assert.DoesNotContain(all, o => o.StartsWith("--vout=", StringComparison.Ordinal));
            Assert.DoesNotContain(all, o => o == ":avcodec-hw=none");
        }
    }
}
