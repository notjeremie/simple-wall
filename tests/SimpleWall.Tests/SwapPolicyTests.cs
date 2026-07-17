using System;
using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    public class SwapPolicyTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Early = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan Late = TimeSpan.FromSeconds(3);

        [Fact]
        public void APictureSwapsImmediately()
        {
            // The fast path: ~286ms on the real machine, and the cut is invisible.
            Assert.Equal(SwapAction.SwapNow, SwapPolicy.Decide(false, 1u, Early, Timeout));
        }

        [Fact]
        public void NoPictureYetKeepsTheOutgoingClipOnTheWall()
        {
            Assert.Equal(SwapAction.KeepWaiting, SwapPolicy.Decide(false, 0u, Early, Timeout));
        }

        /// <summary>
        /// THE one that matters. A clip that is playing but hasn't reported a vout is most likely
        /// just occluded -- an unproven, unprovable-off-hardware question. Abandoning here would
        /// have made that assumption load-bearing: if occluded vouts don't start, every clip
        /// change does nothing and every button on the wall is dead.
        /// </summary>
        [Fact]
        public void NoPictureAfterTheTimeoutSwapsAnywayRatherThanGivingUp()
        {
            var action = SwapPolicy.Decide(false, 0u, Late, Timeout);

            Assert.Equal(SwapAction.SwapAnyway, action);
            Assert.NotEqual(SwapAction.Abandon, action);
        }

        [Fact]
        public void AClipLibVlcCouldNotPlayIsAbandoned()
        {
            // Swapping to it would blank the wall for real, which no amount of waiting fixes.
            Assert.Equal(SwapAction.Abandon, SwapPolicy.Decide(true, 0u, Early, Timeout));
            Assert.Equal(SwapAction.Abandon, SwapPolicy.Decide(true, 0u, Late, Timeout));
        }

        [Fact]
        public void FailureOutranksAPicture()
        {
            // Belt and braces: if libvlc both reported a vout and then errored, the error wins.
            Assert.Equal(SwapAction.Abandon, SwapPolicy.Decide(true, 1u, Early, Timeout));
        }

        [Fact]
        public void TheTimeoutBoundaryDoesNotSwapEarly()
        {
            Assert.Equal(SwapAction.KeepWaiting, SwapPolicy.Decide(false, 0u, Timeout, Timeout));
        }
    }
}
