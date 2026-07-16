using System;
using System.IO;

namespace SimpleWall.Spike
{
    /// <summary>
    /// Resolves a directory the spike can actually write to. The log IS the
    /// deliverable of this trip -- if the app is extracted somewhere like
    /// "C:\Program Files\" or a read-only share, a silent write failure means
    /// the whole VNC round-trip comes back with nothing, while the app looks
    /// perfectly healthy on screen. So this probes with a real write at
    /// startup instead of assuming the EXE's own folder is writable.
    ///
    /// Shared by SpikeForm (log pane + spike-log.txt + vlc-log.txt) and
    /// Program's crash handler, which has no SpikeForm instance to ask.
    /// </summary>
    public static class SpikeLogPaths
    {
        private static readonly object ResolveLock = new object();
        private static string _resolvedDirectory;

        public static string Directory
        {
            get
            {
                lock (ResolveLock)
                {
                    return _resolvedDirectory ?? (_resolvedDirectory = Resolve());
                }
            }
        }

        private static string Resolve()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                baseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "simple-wall-spike"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            foreach (var candidate in candidates)
            {
                if (TryProbeWrite(candidate)) return candidate;
            }

            // Nothing writable was found anywhere we tried. Fall back to the
            // EXE's own folder anyway -- every subsequent write will fail
            // silently from here, but there is nowhere left to try, and
            // returning something deterministic beats throwing during startup.
            return baseDirectory;
        }

        private static bool TryProbeWrite(string directory)
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                var probePath = Path.Combine(directory, ".spike-write-probe.tmp");
                File.WriteAllText(probePath, "probe");
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
