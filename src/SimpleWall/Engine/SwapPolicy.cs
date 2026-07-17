using System;

namespace SimpleWall.Engine
{
    public enum SwapAction
    {
        /// <summary>The incoming clip isn't ready and there's still time. Leave the wall alone.</summary>
        KeepWaiting,

        /// <summary>It has a picture. Swap now -- this is the invisible cut the layers exist for.</summary>
        SwapNow,

        /// <summary>No picture, but no error either. Swap regardless; see the notes on Decide.</summary>
        SwapAnyway,

        /// <summary>libvlc failed on it. Swapping would blank the wall, so don't.</summary>
        Abandon
    }

    /// <summary>
    /// When to flip the layers. Pure, because it's the one branch in the clip-change path where
    /// being wrong is invisible in code review and expensive on the wall.
    /// </summary>
    public static class SwapPolicy
    {
        /// <summary>
        /// The key decision is what "no picture yet" means, and it is NOT "broken".
        ///
        /// Waiting for a picture is only an optimisation: it buys an invisible cut. If the clip
        /// is playing but hasn't reported a video output, the likeliest explanation is that
        /// nobody can see it yet -- whether libvlc builds a Direct3D9 vout against an occluded
        /// window is unproven, and unprovable off the real hardware (--vout=dummy never
        /// increments VoutCount, and the build VM has no GPU). If that is what's happening, the
        /// vout starts the moment the layer comes to the front and the cost is the ~290ms of
        /// black we'd have had with no layers at all.
        ///
        /// So running out of patience degrades to the old behaviour instead of failing. These
        /// are looped background clips: nothing is frame-critical and starting mid-loop costs
        /// nothing, which makes a visible cut a far better answer than a wall that stops
        /// changing. This used to Abandon here, which quietly made an unproven assumption
        /// load-bearing -- if occluded vouts don't start, EVERY clip change would have waited
        /// and then done nothing, leaving every button dead.
        ///
        /// <paramref name="failed"/> outranks everything: a clip libvlc could not play must be
        /// abandoned even if it somehow reported a vout, because swapping to it blanks the wall
        /// for real. That is the only case where the operator is told anything.
        /// </summary>
        public static SwapAction Decide(bool failed, uint voutCount, TimeSpan elapsed, TimeSpan timeout)
        {
            if (failed) return SwapAction.Abandon;
            if (voutCount > 0) return SwapAction.SwapNow;
            return elapsed > timeout ? SwapAction.SwapAnyway : SwapAction.KeepWaiting;
        }
    }
}
