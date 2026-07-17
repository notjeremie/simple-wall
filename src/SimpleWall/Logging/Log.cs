using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;

namespace SimpleWall.Logging
{
    /// <summary>
    /// The append-only text log next to the EXE. This file is the only witness to what happened
    /// at 3am on a Sunday, on a machine nobody is sitting at, that is expected to run for months.
    ///
    /// It lived in Program as a pair of private statics. It is a class now for one reason: it
    /// grew a roll, and a roll is a rename underneath live writers -- which is exactly the sort
    /// of thing that reads fine and is impossible to prove from Program.Main. Here it has tests.
    ///
    /// Three rules, in order of how much they cost when broken:
    ///
    ///   1. **Logging never throws.** Not on a full disk, not on a locked file, not on a
    ///      permissions error. The wall must never stop because of the thing that was only ever
    ///      meant to describe it.
    ///   2. **A failed roll still writes the line.** If the rename can't happen -- a reader has
    ///      the file open, most likely someone tailing it over VNC -- the choice is between
    ///      growing past the ceiling and losing the line. Growing is recoverable; the line is not.
    ///   3. **Writes are serialized.** Not for the append (the OS handles that -- see
    ///      <see cref="Open"/>), but because the roll renames the file that other threads are
    ///      about to open by name. Without the gate, a crash landing mid-roll writes its stack
    ///      into a file that is being renamed out from under it.
    /// </summary>
    public class Log
    {
        /// <summary>~5MB, then roll. Two files, so the worst case on disk is ~10MB.</summary>
        public const long DefaultMaxBytes = 5 * 1024 * 1024;

        public const string DefaultFileName = "simple-wall.log";

        private readonly object _gate = new object();
        private readonly long _maxBytes;

        public Log(string directory, string fileName = DefaultFileName, long maxBytes = DefaultMaxBytes)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            _maxBytes = maxBytes;
            Path = System.IO.Path.Combine(directory, fileName);

            // "simple-wall.log" -> "simple-wall.1.log", so the two sort next to each other and
            // the extension still says what the file is. A reader looking for the log finds both.
            PreviousPath = System.IO.Path.Combine(directory,
                System.IO.Path.GetFileNameWithoutExtension(fileName) + ".1" +
                System.IO.Path.GetExtension(fileName));
        }

        /// <summary>The file being written right now.</summary>
        public string Path { get; }

        /// <summary>The previous file, if one has been rolled. Overwritten by the next roll.</summary>
        public string PreviousPath { get; }

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
        public static Log Open(string fileName = DefaultFileName, long maxBytes = DefaultMaxBytes)
        {
            foreach (var candidate in LogPaths.CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    using (OpenAppend(System.IO.Path.Combine(candidate, fileName))) { }
                    LogPaths.ActiveLogDirectory = candidate;
                    return new Log(candidate, fileName, maxBytes);
                }
                catch
                {
                    // This candidate's log file is unusable -- locked, permissions, whatever.
                    // Try the next rather than silently losing every line for the whole session.
                }
            }

            var fallback = LogPaths.Directory;
            LogPaths.ActiveLogDirectory = fallback;
            return new Log(fallback, fileName, maxBytes);
        }

        public void Write(string message) => Append(Stamp(message));

        /// <summary>
        /// A crash, with the full stack. Same file as everything else, deliberately: the lines
        /// before a crash are most of what makes it readable.
        /// </summary>
        public void WriteCrash(string source, Exception ex) =>
            Append(Stamp($"CRASH via {source}: {ex}"));

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
                    RollIfNeeded();

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
        /// Rolls when the file has already reached the ceiling, checked before the write rather
        /// than after: it costs one stat per line either way, and this way the ceiling is a
        /// ceiling rather than a floor the last line always overshoots.
        ///
        /// Every failure here is swallowed ON PURPOSE, and it is not the same swallow as
        /// <see cref="Append"/>'s. A roll that can't happen -- File.Move needs DELETE access and
        /// our own writers share ReadWrite but not Delete, so anything holding the file open stops
        /// it -- must not cost the caller their line. The log grows past 5MB until whatever is
        /// holding it lets go. That is the right trade: an oversized log is a nuisance, a missing
        /// line is the evidence.
        /// </summary>
        private void RollIfNeeded()
        {
            try
            {
                var info = new FileInfo(Path);
                if (!info.Exists || info.Length < _maxBytes) return;

                // Delete-then-move rather than File.Replace: Replace on a missing destination
                // throws, and the destination is missing on the very first roll.
                if (File.Exists(PreviousPath)) File.Delete(PreviousPath);
                File.Move(Path, PreviousPath);
            }
            catch
            {
                // See the docs above: keep appending past the ceiling rather than lose the line.
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
