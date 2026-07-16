using System;
using System.IO;
using System.Windows.Forms;

namespace SimpleWall
{
    internal static class Program
    {
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

            Application.Run(new SimpleWall.Spike.SpikeForm());
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                // Prefer the directory SpikeForm actually opened spike-log.txt in (set
                // once it succeeds -- see SpikeLogPaths.ActiveLogDirectory), since that
                // can differ from the generic directory-level probe below if the
                // specific file, not just its directory, turned out to be locked. Falls
                // back to the probe when no SpikeForm instance has run yet (a crash
                // before Main gets that far) or none exists any more.
                var directory = SimpleWall.Spike.SpikeLogPaths.ActiveLogDirectory ?? SimpleWall.Spike.SpikeLogPaths.Directory;
                var path = Path.Combine(directory, "spike-log.txt");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} CRASH via {source}: {ex}{Environment.NewLine}";

                // Explicit ReadWrite sharing: SpikeForm holds spike-log.txt open for the
                // whole session via a long-lived StreamWriter (see SpikeForm.OpenLogWriter).
                // File.AppendAllText's default share mode is too narrow to be granted
                // concurrent access while that handle is open, which would otherwise mean
                // a crash-time write gets silently denied at exactly the moment it matters most.
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
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
