using System;

namespace SimpleWall.Engine
{
    /// <summary>
    /// Why a requested slot could not be played. Carries the slot so the UI can point at the
    /// button the operator actually pressed, and the reason so it can say something more
    /// useful than "it didn't work".
    /// </summary>
    public class ClipUnavailableEventArgs : EventArgs
    {
        public ClipUnavailableEventArgs(int slot, string path, string reason)
        {
            Slot = slot;
            Path = path;
            Reason = reason;
        }

        public int Slot { get; }

        /// <summary>The configured path, or null when the slot holds no clip at all.</summary>
        public string Path { get; }

        public string Reason { get; }
    }
}
