using System.Collections.Generic;

namespace SimpleWall.Engine
{
    /// <summary>
    /// Every option string handed to libvlc, in one place, because getting these wrong is
    /// silent and catastrophic on a machine nobody is watching.
    ///
    /// This is data, not behaviour, so the facts below can be asserted by tests instead of
    /// living only in a comment. They are measurements from the real Win7 wall PC on
    /// 2026-07-16, not preferences -- see docs/plans/2026-07-16-spike-findings.md.
    /// </summary>
    public static class VlcOptions
    {
        /// <summary>
        /// The clip repeats this many times before libvlc gives up. It is a COUNTDOWN, not
        /// "forever": measured, plays == repeat + 1 (LibVlcContractTests proves it). At this
        /// value a 30s clip stops after ~22 days, and this app runs unattended for months, so
        /// <see cref="VlcWallEngine"/> ALSO restarts on EndReached. Both are required -- this
        /// option keeps the normal loop seamless, the restart keeps the wall alive past day 22.
        ///
        /// Why not simply a much bigger number? Because it is not known to help. libvlc DOES
        /// accept values above 65535 without complaint (measured: 65536, 70000 and 999999 are
        /// all taken and keep looping), but whether it honours them literally or wraps them to
        /// something SHORTER was not established -- and a wrap would make the wall die sooner,
        /// not later. 65535 is the value the machine has actually been observed to loop on, and
        /// the EndReached restart makes the whole question moot. Do not "optimise" this without
        /// measuring past the wrap point first.
        /// </summary>
        public const int InputRepeat = 65535;

        /// <summary>
        /// Options passed to libvlc itself, read once at construction and never again.
        ///
        /// Deliberately short. The spike shipped with Win7 fallbacks (--vout=direct3d9,
        /// directdraw, forced software decode) built as insurance against problems that
        /// never materialised; the default path works on the real machine, so they are gone.
        ///
        /// NEVER add VLC 2.x logging options (--file-logging, --logfile=, --logmode=). They
        /// do not exist in 3.x, libvlc treats an unknown option as FATAL rather than ignoring
        /// it, and libvlc_new() returns NULL -- i.e. the app opens a window and does nothing,
        /// forever, on arrival at the wall. The 3.x mechanism is LibVLC.SetLogFile() AFTER
        /// construction. LibVlcContractTests constructs a real LibVLC with exactly this list
        /// so that mistake cannot ship twice.
        /// </summary>
        public static string[] LibVlc() => new[]
        {
            // The clips carry an AAC track and VLC was decoding it and routing it to the
            // sound card. Nothing ever told VLC this is a video wall. It is now.
            "--no-audio"
        };

        /// <summary>
        /// Per-media options. <paramref name="repeat"/> is a seam for the contract test that
        /// proves the countdown; production always uses <see cref="InputRepeat"/>.
        /// </summary>
        public static string[] Media(int repeat = InputRepeat)
        {
            var options = new List<string>
            {
                $":input-repeat={repeat}",

                // Belt and braces with --no-audio above: a media-level option survives even
                // if someone later "simplifies" the instance-level one away.
                ":no-audio",

                // Measured: VLC picks a D3D11 decoder against a Direct3D9 display, fails to
                // insert the brightness filter across every chroma combination, tears the
                // vout down, rebuilds on DXVA2, then works -- on every single play. Naming
                // DXVA2 up front skips the failed attempt and lands where VLC ended up
                // anyway. This is an optimisation measured on the wall in Task 15; if it
                // ever misbehaves, deleting this one line restores the proven default path.
                ":avcodec-hw=dxva2"
            };
            return options.ToArray();
        }
    }
}
