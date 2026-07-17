using System;
using System.Globalization;
using System.Security.AccessControl;
using System.IO;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Logging;
using SimpleWall.Model;
using SimpleWall.UI;

namespace SimpleWall
{
    internal static class Program
    {
        private const string LogFile = "simple-wall.log";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A crash on the wall PC must leave evidence in a file, not just an
            // unhandled-exception dialog nobody without a debugger can act on.
            Application.ThreadException += (s, e) => LogCrash("Application.ThreadException", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception
                    ?? new Exception("Non-Exception object thrown: " + e.ExceptionObject));

            // Resolve where the log actually opens before anything can need it. LogPaths'
            // directory probe only proves the DIRECTORY is writable -- its own docs say a caller
            // must try the real file, because a writable directory doesn't mean that file isn't
            // locked. Nothing used to set this, so the probe's answer was taken on faith and a
            // locked log file meant months of silent, swallowed writes.
            LogPaths.ActiveLogDirectory = ResolveLogDirectory();

            var store = new ConfigStore(Path.Combine(LogPaths.ActiveLogDirectory, "config.json"));
            var config = store.Load();
            var library = new ClipLibrary(config.Clips);

            using (var thumbnails = new ThumbnailCache())
            using (var engine = CreateEngine(library, config))
            {
                if (engine == null) return; // already reported; nothing can run without it

                // Saving from the UI on slider release, rather than per scroll tick: a drag
                // fires a hundred times and each Save is an atomic file write.
                Action save = () => store.Save(config);

                Application.Run(new MainForm(engine, library, config, thumbnails, save, Log));

                // Saved once, on the way out. The engine deliberately does not save on every
                // brightness change -- an OSC fader sweep is ~100 packets a second and
                // ConfigStore.Save is an atomic file write. Task 14 owes a debounced save, so
                // that a power cut doesn't lose the session's settings.
                try { store.Save(config); }
                catch (Exception ex) { Log("Saving config on exit failed: " + ex); }
            }
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
                return new VlcWallEngine(library, config, Log);
            }
            catch (Exception ex)
            {
                Log("FATAL: the VLC engine could not start: " + ex);
                MessageBox.Show(
                    "SimpleWall could not start VLC, so the wall cannot run.\n\n" +
                    ex.Message + "\n\nDetails are in simple-wall.log next to the app.",
                    "SimpleWall", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// The first directory where the log file itself will actually open, not merely the
        /// first writable directory. Falls back to the probe's answer, where writes will fail
        /// silently -- but there is nowhere left to try, and refusing to start a video wall over
        /// a log file would be worse.
        /// </summary>
        private static string ResolveLogDirectory()
        {
            foreach (var candidate in LogPaths.CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    using (OpenLog(Path.Combine(candidate, LogFile))) { }
                    return candidate;
                }
                catch
                {
                    // This candidate's log file is unusable -- locked, permissions, whatever.
                    // Try the next rather than silently losing every line for the whole session.
                }
            }

            return LogPaths.Directory;
        }

        /// <summary>
        /// AppendData rather than FileAccess.Write: Write asks for GENERIC_WRITE, so two writers
        /// each seek to the end independently and one can land on top of the other. This asks for
        /// FILE_APPEND_DATA, where the OS does the positioning. It matters because Log and
        /// LogCrash write to the same file from different threads -- i.e. exactly when the log
        /// matters most. ReadWrite sharing so neither locks the other out.
        /// </summary>
        private static FileStream OpenLog(string path) =>
            new FileStream(path, FileMode.Append, FileSystemRights.AppendData,
                FileShare.ReadWrite, 4096, FileOptions.None);

        private static void Log(string message)
        {
            try
            {
                var directory = LogPaths.ActiveLogDirectory ?? LogPaths.Directory;
                var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}";

                using (var stream = OpenLog(Path.Combine(directory, LogFile)))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(line);
                }
            }
            catch
            {
                // Logging must never be the reason the wall stops.
            }
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                var directory = LogPaths.ActiveLogDirectory ?? LogPaths.Directory;
                var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} CRASH via {source}: {ex}{Environment.NewLine}";

                using (var stream = OpenLog(Path.Combine(directory, LogFile)))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(line);
                }
            }
            catch
            {
                // best effort -- there is nothing more we can do if even this fails
            }
        }
    }
}
