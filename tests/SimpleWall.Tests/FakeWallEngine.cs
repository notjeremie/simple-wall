using System;
using System.Collections.Generic;
using SimpleWall.Engine;

namespace SimpleWall.Tests
{
    public class FakeWallEngine : IWallEngine
    {
        public List<WallCommand> Received { get; } = new List<WallCommand>();
        public int? CurrentSlot { get; set; }
        public bool IsPlaying { get; set; }

        // The current clip's look. Settable so a test can stand in for "clip 2 is on the wall at
        // brightness 0.55" without a real engine or library. Default neutral.
        public float CurrentBrightness { get; set; } = AdjustValue.Neutral;
        public float CurrentContrast { get; set; } = AdjustValue.Neutral;
        public event EventHandler StateChanged;
        public event EventHandler<ClipUnavailableEventArgs> ClipUnavailable;

        /// <summary>Lets a test drive the "operator pressed a dead button" path.</summary>
        public void RaiseClipUnavailable(int slot, string path, string reason) =>
            ClipUnavailable?.Invoke(this, new ClipUnavailableEventArgs(slot, path, reason));

        public void Execute(WallCommand command)
        {
            Received.Add(command);
            if (command.Kind == CommandKind.PlayClip)
            {
                CurrentSlot = command.Slot;
                IsPlaying = true;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
