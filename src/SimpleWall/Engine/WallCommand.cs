using SimpleWall.Model;

namespace SimpleWall.Engine
{
    public enum CommandKind { PlayClip, Play, Pause, Toggle, Stop, Brightness, Contrast, Fit }

    public class WallCommand
    {
        public CommandKind Kind { get; set; }
        public int Slot { get; set; }
        public float Value { get; set; }

        public static WallCommand PlayClip(int slot) =>
            new WallCommand { Kind = CommandKind.PlayClip, Slot = slot };
        public static WallCommand Simple(CommandKind kind) => new WallCommand { Kind = kind };
        public static WallCommand WithValue(CommandKind kind, float value) =>
            new WallCommand { Kind = kind, Value = value };
        public static WallCommand Fit(FitMode mode) =>
            new WallCommand { Kind = CommandKind.Fit, Value = (float)mode };
    }
}
