using System;

namespace SimpleWall.Engine
{
    /// <summary>
    /// The single point of contact between the wall and everything that can trigger it.
    /// Mouse clicks, OSC packets from the Stream Deck (Task 7/12), and scheduler ticks
    /// (Task 6/13) all build a <see cref="WallCommand"/> and call <see cref="Execute"/> -
    /// there is no second way in. That is deliberate: a second entry point would be a
    /// second set of bugs, and would let the UI, the OSC listener and the scheduler drift
    /// out of sync about what is actually playing on the wall.
    ///
    /// <see cref="StateChanged"/> is how the UI learns anything happened. It must repaint
    /// itself from <see cref="CurrentSlot"/>/<see cref="IsPlaying"/> in response to this
    /// event, never from its own click handler - otherwise a clip started by the Stream
    /// Deck or the scheduler would trigger the wall but leave the on-screen grid
    /// highlighting the wrong (or no) clip, lying about what is actually on the wall.
    ///
    /// Not thread-safe: all access is expected to happen on the UI thread. The OSC
    /// listener (Task 12) marshals every command onto it, and the scheduler (Task 13)
    /// ticks via a WinForms timer (also the UI thread), so no locking is added here - if
    /// that assumption ever needs to change, it should be a deliberate design change, not
    /// a patch to an implementation of this interface.
    /// </summary>
    public interface IWallEngine
    {
        /// <summary>
        /// The only entry point into the engine. Mouse, OSC and scheduler all call this
        /// and nothing else - see the interface-level docs for why.
        /// </summary>
        void Execute(WallCommand command);

        /// <summary>The slot currently on the wall, or null if nothing has been played yet.</summary>
        int? CurrentSlot { get; }

        /// <summary>Whether the current clip is playing (as opposed to paused).</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Raised after any <see cref="Execute"/> call that may have changed
        /// <see cref="CurrentSlot"/> or <see cref="IsPlaying"/>. The UI must repaint from
        /// those properties on this event rather than from its own click handler, so that
        /// OSC and scheduler triggers keep the grid honest.
        /// </summary>
        event EventHandler StateChanged;

        /// <summary>
        /// Raised when a command named a slot with no clip, or whose file is missing. The
        /// wall is deliberately left untouched in that case, which means nothing on screen
        /// would otherwise change and the operator would be left pressing a dead button with
        /// no idea why. This event is how they find out.
        /// </summary>
        event EventHandler<ClipUnavailableEventArgs> ClipUnavailable;
    }
}
