using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class ConfigStoreTests : IDisposable
    {
        private readonly List<string> _tempPaths = new List<string>();

        private string TempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            _tempPaths.Add(path);
            return path;
        }

        // Quarantine file names are timestamped (config.json.bad-yyyyMMdd-HHmmss-fff), so
        // match by prefix rather than an exact ".bad" suffix.
        private static bool QuarantineFileExists(string path)
        {
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            return Directory.GetFiles(dir, name + ".bad*").Length > 0;
        }

        public void Dispose()
        {
            foreach (var path in _tempPaths)
            {
                TryDelete(path);
                TryDelete(path + ".tmp");

                var dir = Path.GetDirectoryName(path);
                var name = Path.GetFileName(path);
                foreach (var bad in Directory.GetFiles(dir, name + ".bad*"))
                {
                    TryDelete(bad);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* best-effort cleanup only */ }
        }

        [Fact]
        public void LoadReturnsDefaultsWhenFileMissing()
        {
            var config = new ConfigStore(TempFile()).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.Equal(1.0f, config.Brightness);
            Assert.Equal(1.0f, config.Contrast);
            Assert.Empty(config.Clips);
        }

        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            var path = TempFile();
            var store = new ConfigStore(path);
            var config = store.Load();
            config.Brightness = 0.6f;
            config.Clips.Add(new ClipEntry { Slot = 3, Path = @"C:\clips\a.mp4" });
            store.Save(config);

            var loaded = new ConfigStore(path).Load();

            Assert.Equal(0.6f, loaded.Brightness);
            Assert.Equal(3, Assert.Single(loaded.Clips).Slot);
        }

        [Fact]
        public void CorruptConfigIsQuarantinedAndDefaultsReturned()
        {
            var path = TempFile();
            File.WriteAllText(path, "{ this is not json");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.True(QuarantineFileExists(path), "corrupt config should be kept for diagnosis, not deleted");
        }

        [Fact]
        public void EmptyFileIsQuarantined()
        {
            var path = TempFile();
            File.WriteAllText(path, "");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.True(QuarantineFileExists(path), "an empty config deserializes to null and must be treated as corrupt");
        }

        [Fact]
        public void NullClipsDeserializesToEmptyListNotNull()
        {
            var path = TempFile();
            File.WriteAllText(path, "{ \"OscPort\": 7001, \"Clips\": null }");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7001, config.OscPort);
            Assert.NotNull(config.Clips);
            Assert.Empty(config.Clips);
        }

        [Fact]
        public void TruncatedJsonIsQuarantined()
        {
            var path = TempFile();
            File.WriteAllText(path, "{\"OscPort\":7000,\"Clips\":[{\"Slot\"");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7000, config.OscPort);
            Assert.True(QuarantineFileExists(path), "truncated json should be quarantined, not silently replaced with defaults in place");
        }

        [Fact]
        public void UnknownAndMissingFieldsLoadWithDefaultsIntact()
        {
            // Pins forward/backward compatibility: an old config file (missing fields a later
            // task will add, e.g. Task 6's "Tasks" list) or a newer one (with fields this build
            // doesn't know about yet) must both still load, with defaults filling the gaps.
            var path = TempFile();
            File.WriteAllText(path, "{ \"OscPort\": 7005, \"SomeFutureFieldNobodyKnowsAboutYet\": 42 }");

            var config = new ConfigStore(path).Load();

            Assert.Equal(7005, config.OscPort);
            Assert.Equal(1.0f, config.Brightness);
            Assert.NotNull(config.Clips);
            Assert.Empty(config.Clips);
            Assert.NotNull(config.Tasks);
            Assert.Empty(config.Tasks);
            Assert.False(QuarantineFileExists(path), "a merely-old-or-newer config is not corrupt");
        }

        [Fact]
        public void SaveThrowsAndLeavesConfigUntouchedWhenTempFileIsLocked()
        {
            // NOTE: this pins the documented throwing contract and that a failed write to the
            // .tmp file is non-destructive - it does NOT cover the atomic-replace window
            // (File.Replace vs. the old delete-then-move) that CRITICAL 1 fixed. That window
            // is only observable by killing the process mid-Save, which no cheap in-process
            // test can do: locking .tmp makes Save() throw at the temp-write step, before
            // config.json is ever touched, and the old delete-then-move code threw there too.
            // Don't mistake this green for coverage of the atomicity fix.
            var path = TempFile();
            var store = new ConfigStore(path);

            var original = store.Load();
            original.OscPort = 7001;
            store.Save(original);

            // Force the next Save() to fail partway through, by holding an exclusive lock
            // on the temp file it needs to write to.
            var tmpPath = path + ".tmp";
            using (new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var attempted = new WallConfig { OscPort = 9999 };
                Assert.ThrowsAny<IOException>(() => store.Save(attempted));
            }

            var reloaded = new ConfigStore(path).Load();
            Assert.Equal(7001, reloaded.OscPort);
        }

        [Fact]
        public void SavingTwiceLeavesExactlyOneConfigFileAndNoTempFile()
        {
            var path = TempFile();
            var store = new ConfigStore(path);

            var config = store.Load();
            config.Brightness = 0.5f;
            store.Save(config);

            config.Brightness = 0.9f;
            store.Save(config);

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"), "no leftover .tmp file should remain after saving");

            var loaded = new ConfigStore(path).Load();
            Assert.Equal(0.9f, loaded.Brightness);
        }
    }
}
