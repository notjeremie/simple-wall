using System.IO;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class ConfigStoreTests
    {
        private static string TempFile() =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

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
            Assert.True(File.Exists(path + ".bad"), "corrupt config should be kept for diagnosis, not deleted");
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
