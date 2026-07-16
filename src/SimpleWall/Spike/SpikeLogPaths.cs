using System;
using System.Collections.Generic;
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
    /// </summary>
    public static class SpikeLogPaths
    {
        private static readonly object ResolveLock = new object();
        private static string _resolvedDirectory;

        /// <summary>
        /// Ordered list of directories worth trying, cheapest/most obvious first.
        /// SpikeForm walks this itself when opening the actual spike-log.txt handle
        /// (see SpikeForm.OpenLogWriter), because a directory being writable doesn't
        /// guarantee that specific file isn't locked by something else.
        /// </summary>
        public static IEnumerable<string> CandidateDirectories()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (IsUsableFolderPath(localAppData))
                yield return Path.Combine(localAppData, "simple-wall-spike");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (IsUsableFolderPath(desktop))
                yield return desktop;
        }

        /// <summary>
        /// A single resolved, directory-level-writable directory, cached for the
        /// process lifetime. Used by Program's crash handler (best-effort, and it has
        /// no SpikeForm instance to ask directly if a crash happens before one exists
        /// or after one was disposed). SpikeForm prefers ActiveLogDirectory below once
        /// it has actually opened spike-log.txt somewhere, since that can differ from
        /// this directory-level probe if the specific file (not just its directory)
        /// turned out to be locked.
        /// </summary>
        public static string Directory
        {
            get
            {
                lock (ResolveLock)
                {
                    return _resolvedDirectory ?? (_resolvedDirectory = ResolveDirectoryProbe());
                }
            }
        }

        /// <summary>
        /// Set by SpikeForm once it actually succeeds in opening spike-log.txt
        /// somewhere. The crash handler prefers this over the generic directory
        /// probe above when it's set, so a crash lands in the same file the running
        /// session was actually using.
        /// </summary>
        public static string ActiveLogDirectory { get; set; }

        private static string ResolveDirectoryProbe()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var candidate in CandidateDirectories())
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

        /// <summary>
        /// Guards against SpecialFolder lookups returning "" (seen in constrained/
        /// service contexts) -- Path.Combine("", "x") silently yields the relative
        /// path "x", which then resolves against whatever the current working
        /// directory happens to be rather than failing loudly.
        /// </summary>
        private static bool IsUsableFolderPath(string path)
        {
            return !string.IsNullOrEmpty(path) && Path.IsPathRooted(path);
        }
    }
}
