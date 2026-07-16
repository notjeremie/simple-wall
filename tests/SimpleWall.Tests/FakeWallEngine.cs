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
        public event EventHandler StateChanged;

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
