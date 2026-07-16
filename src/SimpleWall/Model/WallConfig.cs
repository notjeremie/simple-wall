using System.Collections.Generic;
using SimpleWall.Scheduling;

namespace SimpleWall.Model
{
    public class WallConfig
    {
        public List<ClipEntry> Clips { get; set; } = new List<ClipEntry>();
        public List<ScheduledTask> Tasks { get; set; } = new List<ScheduledTask>();
        public int OutputX { get; set; }
        public int OutputY { get; set; }

        // Zero means "never configured" -- nobody asks for a zero-sized window -- and
        // GeometryValidator.Resolve turns that into geometry on the LED wall. These must NOT
        // default to a plausible 1920x256 at 0,0: that is a valid-looking window on the
        // OPERATOR'S desktop (the wall is an extended display at X=1920), it survives
        // validation because it really does overlap the primary screen, and the wall just
        // stays dark. First run has to land on the wall.
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public float Brightness { get; set; } = 1.0f;
        public float Contrast { get; set; } = 1.0f;
        public int OscPort { get; set; } = 7000;
        public string OscReplyHost { get; set; } = "";
        public int OscReplyPort { get; set; } = 9000;
        public bool SchedulerEnabled { get; set; } = true;
        public bool Autostart { get; set; }
    }
}
