using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SimpleWall.Logging;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The log is the only witness to what happens on the wall PC at 3am. These tests are about
    /// the two ways it could stop being one: by throwing (and taking the wall with it) and by
    /// growing until it fills the disk.
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

        private Log NewLog(long maxBytes = Log.DefaultMaxBytes) =>
            new Log(_directory, "test.log", maxBytes);

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
        /// The whole reason this class exists. Below the ceiling nothing moves.
        /// </summary>
        [Fact]
        public void DoesNotRollBelowTheCeiling()
        {
            var log = NewLog(maxBytes: 10_000);

            for (var i = 0; i < 20; i++) log.Write(new string('x', 100));

            Assert.True(new FileInfo(log.Path).Length < 10_000);
            Assert.False(File.Exists(log.PreviousPath));
        }

        [Fact]
        public void RollsAtTheCeilingAndKeepsTheOldLinesInTheBackup()
        {
            var log = NewLog(maxBytes: 2_000);

            for (var i = 0; i < 20; i++) log.Write("line " + i + " " + new string('x', 100));

            Assert.True(File.Exists(log.PreviousPath), "the rolled-out file should exist");
            Assert.Contains("line 0", File.ReadAllText(log.PreviousPath));

            // The live file restarted, so it holds the tail rather than the head.
            var live = File.ReadAllText(log.Path);
            Assert.DoesNotContain("line 0 ", live);
            Assert.Contains("line 19", live);
        }

        /// <summary>
        /// Two files, not two hundred. A roll that kept every generation would fill the disk of a
        /// machine expected to run for months just as surely as never rolling at all.
        /// </summary>
        [Fact]
        public void KeepsExactlyOneBackup()
        {
            var log = NewLog(maxBytes: 1_000);

            for (var i = 0; i < 200; i++) log.Write(new string('x', 200));

            var files = Directory.GetFiles(_directory).Select(Path.GetFileName).OrderBy(f => f).ToArray();
            Assert.Equal(new[] { "test.1.log", "test.log" }, files);
        }

        [Fact]
        public void TotalSizeStaysBoundedAtRoughlyTwiceTheCeiling()
        {
            var log = NewLog(maxBytes: 5_000);

            for (var i = 0; i < 500; i++) log.Write(new string('x', 200));

            var total = new FileInfo(log.Path).Length + new FileInfo(log.PreviousPath).Length;

            // One line of slack: the ceiling is checked before a write, so the file that rolls is
            // always at most maxBytes plus the line that tipped it over.
            Assert.True(total <= 2 * 5_000 + 300, $"log grew to {total} bytes");
        }

        /// <summary>
        /// The roll is a rename, and File.Move needs DELETE access -- which our own writers do not
        /// share. Anything holding the file open (someone tailing it over VNC, most likely) stops
        /// the rename. The line must still be written: an oversized log is a nuisance, a missing
        /// line is the evidence.
        /// </summary>
        [Fact]
        public void AReaderHoldingTheFileOpenBlocksTheRollButNotTheWrite()
        {
            var log = NewLog(maxBytes: 500);
            log.Write(new string('x', 600)); // over the ceiling: the next write wants to roll

            // ReadWrite, the way a real tail opens it: it permits our append (which needs write
            // access) but not the roll's File.Move, which opens the source for DELETE and no
            // sharer here grants Delete. That gap is the whole point -- the line survives a roll
            // that can't happen.
            using (File.Open(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                log.Write("written anyway");
            }

            Assert.False(File.Exists(log.PreviousPath), "the roll could not happen");
            Assert.Contains("written anyway", File.ReadAllText(log.Path));
        }

        /// <summary>
        /// Logging must never be the reason the wall stops -- not for a directory that has been
        /// deleted underneath it, and not for anything else.
        /// </summary>
        [Fact]
        public void NeverThrows()
        {
            var log = new Log(Path.Combine(_directory, "gone"), "test.log");

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
        /// Rolling under concurrent load is the case the gate exists for: without it, a writer
        /// opens the path by name while another is renaming it, and a line lands in a file being
        /// moved out from under it.
        ///
        /// The ceiling is chosen so the ~66KB of output rolls EXACTLY ONCE -- big enough that the
        /// current file never refills to the ceiling again. Only then does "across a roll, lose
        /// nothing" have a checkable meaning: a second roll would discard the first generation on
        /// purpose (two files, ~10MB cap -- see KeepsExactlyOneBackup), and asserting no loss
        /// against that would be asserting the design is a bug.
        /// </summary>
        [Fact]
        public void ConcurrentWritersAcrossASingleRollLoseNothing()
        {
            var log = NewLog(maxBytes: 40_000);
            const int writers = 8, each = 100;

            Parallel.For(0, writers, w =>
            {
                for (var i = 0; i < each; i++) log.Write($"writer {w} line {i} " + new string('x', 40));
            });

            Assert.True(File.Exists(log.PreviousPath), "the run should have rolled once");
            var total = File.ReadAllLines(log.Path).Length + File.ReadAllLines(log.PreviousPath).Length;
            Assert.Equal(writers * each, total);
        }

        /// <summary>
        /// "simple-wall.log" rolls to "simple-wall.1.log", so both sort together and both still
        /// look like a log to whoever goes looking for one.
        /// </summary>
        [Fact]
        public void TheBackupSitsNextToTheLogAndKeepsTheExtension()
        {
            var log = new Log(_directory);

            Assert.Equal(Path.Combine(_directory, "simple-wall.log"), log.Path);
            Assert.Equal(Path.Combine(_directory, "simple-wall.1.log"), log.PreviousPath);
        }
    }
}
