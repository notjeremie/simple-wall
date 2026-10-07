using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SimpleWall.Logging;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The log is the only witness to what happens on the wall PC at 3am. These tests are about the
    /// ways it could stop being one: by throwing (and taking the wall with it), by growing until it
    /// fills the disk, and -- the reason the roll was replaced by a file per day -- by deleting the
    /// evidence of the thing you are trying to understand.
    /// </summary>
    public class LogTests : IDisposable
    {
        private readonly string _directory;

        public LogTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "sw-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { }
        }

        private Log NewLog(int retentionDays = Log.DefaultRetentionDays) =>
            new Log(_directory, "test.log", retentionDays);

        private string LogsFolder => Path.Combine(_directory, Log.FolderName);

        /// <summary>Plants a day-file as if the app had run that day.</summary>
        private string PlantDay(Log log, DateTime day, string content = "old line")
        {
            Directory.CreateDirectory(LogsFolder);
            var path = log.PathFor(day);
            File.WriteAllText(path, content + Environment.NewLine);
            return path;
        }

        [Fact]
        public void WritesATimestampedLine()
        {
            var log = NewLog();

            log.Write("hello wall");

            var text = File.ReadAllText(log.Path);
            Assert.Contains("hello wall", text);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} hello wall", text);
        }

        [Fact]
        public void AppendsRatherThanReplacing()
        {
            var log = NewLog();

            log.Write("first");
            log.Write("second");

            Assert.Equal(2, File.ReadAllLines(log.Path).Length);
        }

        /// <summary>
        /// The shape of the whole change: a folder, and a file named for its day in ISO order so
        /// the folder sorts chronologically.
        /// </summary>
        [Fact]
        public void WritesIntoADatedFileInALogsFolder()
        {
            var log = NewLog();

            log.Write("today");

            Assert.Equal(Path.Combine(_directory, "logs"), log.LogDirectory);
            Assert.Equal(Path.Combine(LogsFolder, "test-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), log.Path);
            Assert.True(File.Exists(log.Path));
        }

        /// <summary>
        /// config.json lives in Directory (see Program). If this ever starts pointing at the logs
        /// folder, the wall silently loses every clip and schedule it has.
        /// </summary>
        [Fact]
        public void TheAppDirectoryIsNotTheLogFolder()
        {
            var log = NewLog();

            Assert.Equal(_directory, log.Directory);
            Assert.NotEqual(log.Directory, log.LogDirectory);
        }

        /// <summary>Yesterday's lines stay in yesterday's file. That is the entire point.</summary>
        [Fact]
        public void EachDayIsItsOwnFile()
        {
            var log = NewLog();
            var yesterday = PlantDay(log, DateTime.Now.AddDays(-1), "yesterday's evidence");

            log.Write("today's line");

            Assert.Contains("yesterday's evidence", File.ReadAllText(yesterday));
            Assert.DoesNotContain("today's line", File.ReadAllText(yesterday));
            Assert.Contains("today's line", File.ReadAllText(log.Path));
        }

        /// <summary>
        /// The old 5MB roll would discard the record of a rare incident. Nothing here has a size
        /// ceiling, so a big day stays whole.
        /// </summary>
        [Fact]
        public void ThereIsNoSizeCeilingWithinADay()
        {
            var log = NewLog();

            for (var i = 0; i < 500; i++) log.Write("line " + i + " " + new string('x', 200));

            var lines = File.ReadAllLines(log.Path);
            Assert.Equal(500, lines.Length);
            Assert.Contains("line 0 ", lines[0]);
            Assert.Contains("line 499 ", lines[499]);
        }

        [Fact]
        public void SweepDeletesFilesPastTheRetentionWindow()
        {
            var log = NewLog(retentionDays: 30);
            var ancient = PlantDay(log, DateTime.Now.AddDays(-90));
            var stale = PlantDay(log, DateTime.Now.AddDays(-31));

            var deleted = log.Sweep();

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(ancient));
            Assert.False(File.Exists(stale));
        }

        [Fact]
        public void SweepKeepsFilesInsideTheRetentionWindow()
        {
            var log = NewLog(retentionDays: 30);
            var recent = PlantDay(log, DateTime.Now.AddDays(-29));
            var today = PlantDay(log, DateTime.Now);

            log.Sweep();

            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(today));
        }

        /// <summary>
        /// The one that matters most in this file. Sweep is the only code in the app that deletes
        /// anything, it runs unattended, and it runs in a folder a human may have dropped things
        /// into. The wildcard matches these names; the date parse is what saves them.
        /// </summary>
        [Fact]
        public void SweepNeverDeletesAnythingItCannotDate()
        {
            var log = NewLog(retentionDays: 1);
            Directory.CreateDirectory(LogsFolder);

            var notes = Path.Combine(LogsFolder, "test-notes.log");
            var empty = Path.Combine(LogsFolder, "test-.log");
            var nearly = Path.Combine(LogsFolder, "test-2026-8-4.log");   // not zero-padded ISO
            var other = Path.Combine(LogsFolder, "something-else.txt");
            foreach (var f in new[] { notes, empty, nearly, other }) File.WriteAllText(f, "keep me");

            var deleted = log.Sweep();

            Assert.Equal(0, deleted);
            Assert.All(new[] { notes, empty, nearly, other }, f => Assert.True(File.Exists(f), f + " was deleted"));
        }

        /// <summary>
        /// This machine's clock is known to leap -- a flat CMOS battery boots it in 2019 and
        /// w32time corrects it later (see TickGuard). While it is wrong-and-early, every real file
        /// looks like it is in the future, and must be kept rather than swept.
        /// </summary>
        [Fact]
        public void SweepKeepsFilesFromTheFutureWhenTheClockIsWrong()
        {
            var log = NewLog(retentionDays: 30);
            var future = PlantDay(log, DateTime.Now.AddYears(7));

            log.Sweep();

            Assert.True(File.Exists(future));
        }

        /// <summary>
        /// The wall PC's old single simple-wall.log sits in the app directory and holds the record
        /// of the 07-30 hang. Sweep works inside the logs folder and must never reach up to it.
        /// </summary>
        [Fact]
        public void SweepNeverTouchesAnythingOutsideTheLogsFolder()
        {
            var log = NewLog(retentionDays: 1);
            var legacy = Path.Combine(_directory, "test.log");
            var config = Path.Combine(_directory, "config.json");
            File.WriteAllText(legacy, "the 07-30 evidence");
            File.WriteAllText(config, "{}");

            log.Sweep();

            Assert.True(File.Exists(legacy), "the pre-existing log was deleted");
            Assert.True(File.Exists(config), "config.json was deleted");
        }

        [Fact]
        public void SweepOnAnAbsentFolderIsHarmless()
        {
            var log = NewLog();

            Assert.Equal(0, log.Sweep());
        }

        /// <summary>
        /// Logging must never be the reason the wall stops -- not for a directory that has been
        /// deleted underneath it, and not for anything else.
        /// </summary>
        [Fact]
        public void NeverThrows()
        {
            // A path no directory can be created at: a file where a folder would have to go.
            var blocker = Path.Combine(_directory, "blocker");
            File.WriteAllText(blocker, "not a directory");
            var log = new Log(blocker, "test.log");

            var ex = Record.Exception(() => log.Write("into the void"));

            Assert.Null(ex);
        }

        [Fact]
        public void WriteCrashCarriesTheSourceAndTheFullStack()
        {
            var log = NewLog();
            Exception thrown;
            try { throw new InvalidOperationException("libvlc said no"); }
            catch (Exception ex) { thrown = ex; }

            log.WriteCrash("AppDomain.UnhandledException", thrown);

            var text = File.ReadAllText(log.Path);
            Assert.Contains("CRASH via AppDomain.UnhandledException", text);
            Assert.Contains("libvlc said no", text);
            Assert.Contains(nameof(WriteCrashCarriesTheSourceAndTheFullStack), text); // the stack
        }

        /// <summary>
        /// Ordinary lines and crash stacks come from different threads -- i.e. the log matters most
        /// exactly when it is contended. Nothing may be lost or interleaved into a torn line.
        /// </summary>
        [Fact]
        public void ConcurrentWritersLoseNothingAndTearNothing()
        {
            var log = NewLog();
            const int writers = 8, each = 200;

            Parallel.For(0, writers, w =>
            {
                for (var i = 0; i < each; i++) log.Write($"writer {w} line {i}");
            });

            var lines = File.ReadAllLines(log.Path);
            Assert.Equal(writers * each, lines.Length);
            Assert.All(lines, line => Assert.Matches(@"^\d{4}-\d{2}-\d{2} .* writer \d line \d+$", line));
        }

        /// <summary>
        /// A crash stack is far larger than a StreamWriter buffer, so one WriteCrash is several
        /// WriteFile calls. Without the gate, two of them at once interleave into a torn stack --
        /// which is the reason the lock survived the removal of the roll it was originally for.
        /// </summary>
        [Fact]
        public void ConcurrentCrashStacksDoNotInterleave()
        {
            var log = NewLog();
            Exception thrown;
            try { throw new InvalidOperationException("libvlc said no " + new string('y', 4000)); }
            catch (Exception ex) { thrown = ex; }

            Parallel.For(0, 8, _ => log.WriteCrash("AppDomain.UnhandledException", thrown));

            // Every line begins with a stamp or is a stack continuation -- never a stamp spliced
            // into the middle of another writer's stack.
            var lines = File.ReadAllLines(log.Path);
            Assert.Equal(8, lines.Count(l => l.Contains("CRASH via")));
            Assert.All(lines.Where(l => l.Contains("CRASH via")),
                l => Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} CRASH via ", l));
        }

        /// <summary>
        /// Open() is what production calls, and it must leave ActiveLogDirectory pointing at the
        /// APP directory -- config.json is resolved from it -- not at the logs folder.
        /// </summary>
        [Fact]
        public void OpenLeavesTheConfigDirectoryPointingAtTheAppFolder()
        {
            var log = Log.Open();

            Assert.Equal(log.Directory, LogPaths.ActiveLogDirectory);
            Assert.Equal(Path.Combine(log.Directory, "logs"), log.LogDirectory);
            Assert.True(Directory.Exists(log.LogDirectory), "Open should have created the logs folder");
        }
    }
}
