using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;

namespace SimpleWall.Logging
{
    /// <summary>
    /// The append-only text log, one file per day, in a "logs" folder next to the EXE. This is the
    /// only witness to what happened at 3am on a Sunday, on a machine nobody is sitting at, that is
    /// expected to run for months.
    ///
    /// It used to be a single file that rolled at 5MB into one backup. That was replaced on
    /// 2026-08-05 after the 07-30 hang, for two reasons the incident made concrete:
    ///
    ///   1. **The roll destroys evidence.** Incidents here are weeks apart, and the whole value of
    ///      this file is being readable after one. A ~10MB two-file ceiling means a busy month
    ///      silently discards the record of the thing you are trying to understand. Diagnosing
    ///      07-30 meant reading 672 lines spanning 14 days and three deployments; "what happened on
    ///      Thursday" should be one file.
    ///   2. **The roll was the dangerous part of this class.** It was a rename underneath live
    ///      writers -- the sort of thing that reads fine and is very hard to prove. A per-day
    ///      filename is computed from the clock at each write, so there is no rename, no delete of
    ///      a live file, and no window in which a writer opens a name that is moving. That code is
    ///      gone rather than fixed.
    ///
    /// Three rules survive from the old design, in order of how much they cost when broken:
    ///
    ///   1. **Logging never throws.** Not on a full disk, not on a locked file, not on a
    ///      permissions error. The wall must never stop because of the thing that was only ever
    ///      meant to describe it.
    ///   2. **Growing beats losing a line.** There is no per-file ceiling. A day is bounded by how
    ///      much the app writes in a day (~30KB in normal service), the total is bounded by
    ///      <see cref="DefaultRetentionDays"/>, and a pathological day is a fault worth having the
    ///      evidence of. An oversized log is a nuisance; a missing line is the evidence.
    ///   3. **Writes are serialized.** NOT for the append -- the OS handles that, see
    ///      <see cref="OpenAppend"/> -- and no longer for a roll, which no longer exists. The gate
    ///      is kept because a crash stack is far bigger than a StreamWriter buffer, so one
    ///      WriteCrash is several WriteFile calls, and two threads doing that at once would
    ///      interleave into a torn stack. That is exactly the moment the log matters most.
    /// </summary>
    public class Log
    {
        /// <summary>
        /// How long a day's file is kept. Ninety days of normal service is roughly 3MB total, so
        /// this is generous rather than tight -- the point is that an unattended machine should not
        /// have an unbounded anything, not that disk is short.
        ///
        /// Not configurable, and that is a constraint rather than a decision: Program opens the log
        /// BEFORE it loads config.json (it wants somewhere to report a config failure TO), so there
        /// is no config to read at the moment this is needed.
        /// </summary>
        public const int DefaultRetentionDays = 90;

        public const string DefaultFileName = "simple-wall.log";

        /// <summary>The subfolder, so the app directory does not accumulate a file per day.</summary>
        public const string FolderName = "logs";

        private readonly object _gate = new object();
        private readonly string _stem;
        private readonly string _extension;
        private readonly int _retentionDays;

        public Log(string directory, string fileName = DefaultFileName, int retentionDays = DefaultRetentionDays)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A file name is required.", nameof(fileName));
            if (retentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));

            Directory = directory;
            LogDirectory = System.IO.Path.Combine(directory, FolderName);
            _stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            _extension = System.IO.Path.GetExtension(fileName);
            _retentionDays = retentionDays;
        }

        /// <summary>
        /// The directory the app resolved as writable -- NOT where the log files are.
        ///
        /// This is load-bearing beyond logging: config.json lives here (see Program), so it must go
        /// on meaning "the app's working directory" now that the log files have moved down into
        /// <see cref="LogDirectory"/>. Pointing this at the logs folder would quietly relocate the
        /// wall's configuration and lose every clip and schedule on it.
        /// </summary>
        public string Directory { get; }

        /// <summary>The "logs" folder the daily files live in.</summary>
        public string LogDirectory { get; }

        /// <summary>Today's file. A property, not a field: at midnight it starts naming a new one.</summary>
        public string Path => PathFor(DateTime.Now);

        /// <summary>
        /// A given day's file: "simple-wall-2026-08-04.log". ISO order so the folder sorts
        /// chronologically by name, and the extension kept so it still looks like a log to whoever
        /// goes looking for one.
        /// </summary>
        public string PathFor(DateTime day) =>
            System.IO.Path.Combine(LogDirectory,
                _stem + "-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + _extension);

        /// <summary>
        /// Opens a <see cref="Log"/> where the log file itself will actually open -- not merely in
        /// the first writable directory.
        ///
        /// LogPaths' probe only proves the DIRECTORY is writable, and its own docs say a caller
        /// must try the real file, because a writable directory doesn't mean that file isn't
        /// locked. Nothing used to do that, so the probe's answer was taken on faith and a locked
        /// log file meant months of silent, swallowed writes.
        ///
        /// Falls back to the probe's answer, where writes will fail silently -- but there is
        /// nowhere left to try, and refusing to start a video wall over a log file would be worse.
        /// </summary>
        public static Log Open(string fileName = DefaultFileName, int retentionDays = DefaultRetentionDays)
        {
            foreach (var candidate in LogPaths.CandidateDirectories())
            {
                try
                {
                    var log = new Log(candidate, fileName, retentionDays);
                    System.IO.Directory.CreateDirectory(log.LogDirectory);

                    // Today's real file, not just the folder: see the note above about a writable
                    // directory proving nothing about a locked file.
                    using (OpenAppend(log.Path)) { }

                    // The CANDIDATE, not the logs folder -- config.json lives here. See Directory.
                    LogPaths.ActiveLogDirectory = candidate;

                    log.Sweep();
                    return log;
                }
                catch
                {
                    // This candidate's log file is unusable -- locked, permissions, whatever.
                    // Try the next rather than silently losing every line for the whole session.
                }
            }

            var fallback = LogPaths.Directory;
            LogPaths.ActiveLogDirectory = fallback;
            return new Log(fallback, fileName, retentionDays);
        }

        public void Write(string message) => Append(Stamp(message));

        /// <summary>
        /// A crash, with the full stack. Same file as everything else, deliberately: the lines
        /// before a crash are most of what makes it readable.
        /// </summary>
        public void WriteCrash(string source, Exception ex) =>
            Append(Stamp($"CRASH via {source}: {ex}"));

        /// <summary>
        /// Deletes day-files older than the retention window. Returns how many went, for the tests.
        ///
        /// Run once from <see cref="Open"/> rather than on a timer. This app can run for months
        /// without restarting, so a long run does accumulate a file per day and sweeps only on the
        /// next launch -- which is fine, because 200 unswept days is about 6MB. A timer would be
        /// more code for less than nothing.
        ///
        /// TWO conditions before anything is deleted, not one: the name must match this log's own
        /// stem and extension, AND the date in the middle must parse exactly. A file that does not
        /// parse is left alone forever. That is deliberate -- this is the only code in the app that
        /// deletes anything, it runs unattended, and it runs in a folder a human may well have
        /// dropped something into.
        ///
        /// Note what it CANNOT touch: anything outside the logs folder. The old single
        /// simple-wall.log in the app directory -- which on the wall PC holds the record of the
        /// 07-30 hang -- is not in here and is never a candidate.
        ///
        /// Safe against this machine's known clock jump (a flat CMOS battery boots it in 2019; see
        /// TickGuard). If the clock is wrong-and-early, every real file looks like it is in the
        /// FUTURE rather than old, so nothing is deleted. The error direction is "keeps too much",
        /// which is the harmless one.
        /// </summary>
        public int Sweep()
        {
            var deleted = 0;
            try
            {
                if (!System.IO.Directory.Exists(LogDirectory)) return 0;

                var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);

                foreach (var file in System.IO.Directory.GetFiles(LogDirectory, _stem + "-*" + _extension))
                {
                    var day = DayFromName(System.IO.Path.GetFileName(file));
                    if (day == null || day.Value >= cutoff) continue;

                    try { File.Delete(file); deleted++; }
                    catch { /* locked by a reader, most likely. It can go next launch. */ }
                }
            }
            catch
            {
                // Retention is housekeeping. It must never be the reason the wall fails to start.
            }
            return deleted;
        }

        /// <summary>
        /// The date in "simple-wall-2026-08-04.log", or null if this is not one of ours. The
        /// wildcard in <see cref="Sweep"/> would also match "simple-wall-notes.log" and
        /// "simple-wall-.log"; this is what stops them being deleted.
        /// </summary>
        private DateTime? DayFromName(string name)
        {
            var prefix = _stem + "-";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (!name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase)) return null;

            var middle = name.Substring(prefix.Length, name.Length - prefix.Length - _extension.Length);

            return DateTime.TryParseExact(middle, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) ? day : (DateTime?)null;
        }

        private static string Stamp(string message) =>
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
            " " + message + Environment.NewLine;

        private void Append(string line)
        {
            try
            {
                // Reentrant, so a crash handler firing on a thread that is already inside this
                // method re-enters rather than deadlocking against itself.
                lock (_gate)
                {
                    // Every write, not once at startup: this app runs for months, and the folder
                    // can be deleted underneath it by a human tidying up over VNC. Costs one
                    // syscall per line, which is what the old ceiling check cost too, at about one
                    // line a second.
                    System.IO.Directory.CreateDirectory(LogDirectory);

                    using (var stream = OpenAppend(Path))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(line);
                    }
                }
            }
            catch
            {
                // Logging must never be the reason the wall stops.
            }
        }

        /// <summary>
        /// AppendData rather than FileAccess.Write: Write asks for GENERIC_WRITE, so two writers
        /// each seek to the end independently and one can land on top of the other. This asks for
        /// FILE_APPEND_DATA, where the OS does the positioning. It matters because ordinary lines
        /// and crash stacks are written from different threads -- i.e. exactly when the log
        /// matters most. ReadWrite sharing so neither locks the other out, and so someone can tail
        /// the file while the wall runs.
        /// </summary>
        private static FileStream OpenAppend(string path) =>
            new FileStream(path, FileMode.Append, FileSystemRights.AppendData,
                FileShare.ReadWrite, 4096, FileOptions.None);
    }
}
