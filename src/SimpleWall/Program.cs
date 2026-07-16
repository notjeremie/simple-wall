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
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spike-log.txt");
                var line = $"{DateTime.Now:HH:mm:ss.fff} CRASH via {source}: {ex}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
            catch
            {
                // best effort -- there is nothing more we can do if even this fails
            }
        }
    }
}
