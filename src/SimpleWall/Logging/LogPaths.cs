using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleWall.Logging
{
    /// <summary>
    /// Resolves a directory the app can actually write to. If the app is installed
    /// somewhere like "C:\Program Files\" or run from a read-only share, a silent
    /// write failure means a wall PC that misbehaves for months leaves no evidence,
    /// while looking perfectly healthy on screen. So this probes with a real write
    /// instead of assuming the EXE's own folder is writable.
    ///
    /// Inherited from the spike, which is otherwise gone: this part was right, and the
    /// reasoning survives the trip it was written for.
    /// </summary>
    public static class LogPaths
    {
        private static readonly object ResolveLock = new object();
        private static string _resolvedDirectory;

        /// <summary>
        /// Ordered list of directories worth trying, cheapest/most obvious first.
        /// A caller opening the real log file should walk this itself rather than trusting
        /// the probe below, because a directory being writable doesn't guarantee that
        /// specific file isn't locked by something else.
        /// </summary>
        public static IEnumerable<string> CandidateDirectories()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (IsUsableFolderPath(localAppData))
                yield return Path.Combine(localAppData, "simple-wall");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (IsUsableFolderPath(desktop))
                yield return desktop;
        }

        /// <summary>
        /// A single resolved, directory-level-writable directory, cached for the process
        /// lifetime. Used by Program's crash handler, which is best-effort by nature: a
        /// crash can happen before any window exists or after one is gone, so it has
        /// nothing to ask and must resolve somewhere on its own. Prefer
        /// <see cref="ActiveLogDirectory"/> when it is set -- it can differ from this
        /// directory-level probe if the specific file, not just its directory, was locked.
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
        /// Set by whoever actually succeeds in opening the log file somewhere. The crash
        /// handler prefers this over the generic directory probe above when it's set, so a
        /// crash lands in the same file the running session was using.
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
                var probePath = Path.Combine(directory, ".simple-wall-write-probe.tmp");
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
