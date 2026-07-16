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
        public int OutputWidth { get; set; } = 1920;
        public int OutputHeight { get; set; } = 256;
        public float Brightness { get; set; } = 1.0f;
        public float Contrast { get; set; } = 1.0f;
        public int OscPort { get; set; } = 7000;
        public string OscReplyHost { get; set; } = "";
        public int OscReplyPort { get; set; } = 9000;
        public bool SchedulerEnabled { get; set; } = true;
        public bool Autostart { get; set; }
    }
}
