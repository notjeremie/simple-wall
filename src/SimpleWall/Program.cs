using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Logging;
using SimpleWall.Model;
using SimpleWall.Osc;
using SimpleWall.Scheduling;
using SimpleWall.UI;

namespace SimpleWall
{
    internal static class Program
    {
        /// <summary>
        /// Local\, not Global\: this is about one operator's session on one wall PC, autostart
        /// (HKCU\Run, so the interactive session) racing someone double-clicking the icon. Global\
        /// would additionally need SeCreateGlobalPrivilege, which is a way to fail at startup for
        /// no benefit.
        ///
        /// A GUID rather than "SimpleWall": a kernel object name is machine-wide within the
        /// session namespace, and colliding with some other program's mutex would mean this app
        /// silently refuses to start with no way to find out why.
        /// </summary>
        private const string InstanceMutex = @"Local\SimpleWall-{6a1f4c8e-3d92-4b17-9c5a-0e8f2d6b4a13}";

        private static Log _log;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // initiallyOwned: false, because nothing here ever waits on it. `createdNew` alone
            // answers the only question -- does another process hold a handle to this name -- and
            // not owning it means a hard-killed first instance can never leave an abandoned mutex
            // for the next launch to trip over.
            bool createdNew;
            using (var instance = new Mutex(false, InstanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    // A dialog, unlike the crash handler's deliberate silence: this only ever
                    // happens because a human just double-clicked the icon, so there is someone
                    // there to read it. Saying nothing would have them double-click again.
                    MessageBox.Show(
                        "SimpleWall is already running on this machine.\n\n" +
                        "Look for its window, or check the notification area.",
                        "SimpleWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Run();

                // The mutex must outlive everything above it -- the JIT is entitled to collect an
                // object whose last use has passed, even inside its own `using`.
                GC.KeepAlive(instance);
            }
        }

        private static void Run()
        {
            // Resolved before anything can need it: Log.Open walks the candidate directories and
            // opens the real file at each, because a writable DIRECTORY does not mean that file
            // isn't locked. This used to be taken on faith and a locked log meant months of
            // silent, swallowed writes.
            _log = Log.Open();

            // A crash on the wall PC must leave evidence in a file, not just an
            // unhandled-exception dialog nobody without a debugger can act on.
            //
            // ThreadException is the UI thread's backstop for the UNanticipated -- the tick and
            // the event handlers already catch what they expect. Left to itself, a registered
            // ThreadException handler swallows the exception and RESUMES the message loop, which
            // sounds gentle and is not: an exception that recurs at message-loop rate (a broken
            // paint, a repeating layout fault) would then log on every pump, rolling the log over
            // and over, wall visibly broken, forever, with nothing on screen and no restart. So
            // this honours the spec's "then let it die": log the full stack, then exit. A clean
            // process death an autostart brings back at next logon beats an unattended machine
            // limping in an undefined state. Environment.Exit, not Application.Exit, because the
            // state is already unknown and running dispose/save paths through it could do harm --
            // the OS reclaims the window and the VLC natives regardless.
            Application.ThreadException += (s, e) =>
            {
                _log.WriteCrash("Application.ThreadException", e.Exception);
                Environment.Exit(1);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                _log.WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception
                    ?? new Exception("Non-Exception object thrown: " + e.ExceptionObject));

            WriteLog($"SimpleWall starting. Log: {_log.Path}");

            var store = new ConfigStore(Path.Combine(LogPaths.ActiveLogDirectory, "config.json"));
            var config = store.Load();
            var library = new ClipLibrary(config.Clips);
            var scheduler = new Scheduler(config.Tasks) { Enabled = config.SchedulerEnabled };

            using (var thumbnails = new ThumbnailCache())
            using (var engine = CreateEngine(library, config))
            {
                if (engine == null) return; // already reported; nothing can run without it

                // Saving from the UI on slider release, rather than per scroll tick: a drag
                // fires a hundred times and each Save is an atomic file write.
                Action save = () =>
                {
                    // The scheduler's master switch lives on the Scheduler at runtime; the config
                    // is what survives a restart, so it has to be told before every save.
                    config.SchedulerEnabled = scheduler.Enabled;
                    store.Save(config);
                };

                using (var form = new MainForm(engine, library, scheduler, config, thumbnails, save, WriteLog,
                    engine.ApplyGeometry))
                using (var listener = new OscListener(config.OscPort, form, WriteLog))
                using (var replies = new OscReplySender(engine, config, WriteLog))
                {
                    // The listener marshals onto the form, so this handler is already on the UI
                    // thread by the time it runs -- which is what the engine requires.
                    //
                    // A clip trigger is logged with its OSC source, per the log's contract. ONLY a
                    // clip trigger: a Stream Deck fader sweep is ~100 brightness packets a second,
                    // and a log line each would bury the evening in noise and roll the file over
                    // in minutes. Brightness/contrast reaching the wall is not the question the
                    // log exists to answer; who changed which clip is.
                    listener.CommandReceived += (s, command) =>
                    {
                        if (command.Kind == CommandKind.PlayClip)
                            WriteLog($"Clip {command.Slot} triggered (OSC)");
                        engine.Execute(command);
                    };

                    // Captured as well as logged, so the settings tab can say WHY OSC is off
                    // rather than just that it is. Raised synchronously from Start below.
                    string oscFailure = null;
                    listener.Failed += (s, message) => { WriteLog(message); oscFailure = message; };

                    // Force the window's handle to exist BEFORE anything can arrive. The listener
                    // drops commands while there is no handle to marshal onto (it will not run
                    // them on the receive thread), and Application.Run below is what would
                    // otherwise create it -- so an autostarted wall PC and an eager Stream Deck
                    // would lose presses on every boot.
                    GC.KeepAlive(form.Handle);

                    var listening = listener.Start();
                    replies.Start();

                    // After Start, never before: the settings tab reports the port actually bound,
                    // and the whole point of that line is that it cannot be a guess.
                    form.ReportOscStatus(listening ? listener.BoundPort : -1, oscFailure);

                    Application.Run(form);
                }

                // Saved once, on the way out, and through `save` rather than store.Save directly:
                // `save` is what copies scheduler.Enabled into the config, and this is the LAST
                // write, so bypassing it would persist stale state the moment anything other than
                // the master checkbox can toggle the schedule (an OSC address, say -- OSC already
                // reaches Execute).
                try { save(); }
                catch (Exception ex) { WriteLog("Saving config on exit failed: " + ex); }
            }

            WriteLog("SimpleWall stopped.");
        }

        /// <summary>
        /// The engine is the app: without it there is no wall. If it can't start, SAY SO on
        /// screen. This is the "libvlc_new returned NULL" story in its current form -- it would
        /// otherwise throw before the message loop exists, so Application.ThreadException never
        /// sees it, and an autostarted wall PC would show the operator nothing whatsoever: no
        /// window, no dialog, just a machine that appears not to have started.
        /// </summary>
        private static VlcWallEngine CreateEngine(ClipLibrary library, WallConfig config)
        {
            try
            {
                return new VlcWallEngine(library, config, WriteLog);
            }
            catch (Exception ex)
            {
                WriteLog("FATAL: the VLC engine could not start: " + ex);
                MessageBox.Show(
                    "SimpleWall could not start VLC, so the wall cannot run.\n\n" +
                    ex.Message + "\n\nDetails are in simple-wall.log next to the app.",
                    "SimpleWall", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// The Action&lt;string&gt; every component logs through. A method rather than
        /// `_log.Write` directly, so a line written before Log.Open (or after a failure to open
        /// one) is dropped rather than thrown from.
        /// </summary>
        private static void WriteLog(string message) => _log?.Write(message);
    }
}
