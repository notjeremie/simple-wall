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

        // The clip SLOT the wall boots into. 0 = none (the wall starts dark -- the historical
        // behaviour). Set from the grid's right-click "Make this clip default". Played once on
        // launch (MainForm.OnShown), so an unattended wall coming back from a power cut shows
        // content instead of a black wall until the next scheduled task fires -- autostart brings
        // the app back, this puts a picture on the wall. Slot, not clip identity, because slots
        // travel with their clip (ClipLibrary.Move) and the whole app already addresses clips by
        // slot. Honoured only if the slot still holds a clip whose file exists; otherwise the wall
        // is left dark and the reason is logged, never guessed.
        public int DefaultSlot { get; set; }

        // Seconds of UI-thread silence before the watchdog restarts the app; 0 switches it off.
        // See Infrastructure/WatchdogPolicy for the 2026-07-30 hang this answers. Absent from an
        // older config.json, so an existing install picks this default up and is protected without
        // anyone editing anything.
        //
        // Two minutes, i.e. 120 beats of a one-second heartbeat, and the size is deliberate. The
        // two error cases are nothing like each other: noticing late costs a couple of extra
        // minutes on a wall that was already stuck, while a false positive restarts a wall that
        // was working, mid-programme, for no reason.
        //
        // 120 rather than 60 because of where the clips live. They are on a network share
        // (V:\VIZRT\...), and VlcWallEngine.PlayClip calls File.Exists on that path ON THE UI
        // THREAD -- against a share that has dropped, one such call can block for tens of seconds
        // before it returns false. Two of them in a single scheduler tick would clear 60s and
        // restart a wall whose only problem was a slow file server.
        //
        // Configurable, not a constant, for the case this cannot anticipate: if it ever does
        // restart a healthy wall, whoever is standing in front of it needs to be able to switch it
        // off in config.json without waiting for a rebuild.
        public int WatchdogSeconds { get; set; } = 120;

        // There is deliberately no Autostart field here. Autostart is an HKCU\...\Run value, and
        // the registry is the ONLY thing that decides whether Windows launches this app -- a bool
        // in here could only ever be a second opinion. It would disagree with the truth the first
        // time anyone touched msconfig or Task Manager's Startup tab, and this app would then show
        // a ticked box for a machine that never comes back after a reboot. See Infrastructure/
        // Autostart. (One did exist, was read by nothing, and is gone.)
    }
}
