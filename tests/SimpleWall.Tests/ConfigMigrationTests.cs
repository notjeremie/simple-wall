using System.Collections.Generic;
using SimpleWall.Engine;
using SimpleWall.Model;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The one-time move of a pre-look config's global brightness/contrast onto each clip. The
    /// point is that the wall looks IDENTICAL the first time the new build runs, and that the
    /// migration is a no-op on every launch after -- otherwise the un-reset global would re-seed
    /// every clip and wipe per-clip looks set in between.
    /// </summary>
    public class ConfigMigrationTests
    {
        private static WallConfig ConfigWith(float globalBrightness, float globalContrast, params int[] slots)
        {
            var clips = new List<ClipEntry>();
            foreach (var slot in slots) clips.Add(new ClipEntry { Slot = slot, Path = $@"V:\{slot}.mp4" });
            return new WallConfig { Brightness = globalBrightness, Contrast = globalContrast, Clips = clips };
        }

        [Fact]
        public void ANonNeutralGlobalIsCopiedOntoEveryClipAndThenReset()
        {
            var config = ConfigWith(0.6f, 1.4f, 1, 2, 3);

            Assert.True(ConfigMigration.SeedClipLooks(config));

            foreach (var clip in config.Clips)
            {
                Assert.Equal(0.6f, clip.Brightness);
                Assert.Equal(1.4f, clip.Contrast);
            }
            // Reset so the next launch is a no-op.
            Assert.Equal(ClipEntry.NeutralLook, config.Brightness);
            Assert.Equal(ClipEntry.NeutralLook, config.Contrast);
        }

        [Fact]
        public void ASecondRunIsANoOpAndLeavesPerClipLooksAlone()
        {
            var config = ConfigWith(0.6f, 1.4f, 1, 2);
            ConfigMigration.SeedClipLooks(config);           // first run
            config.Clips[0].Brightness = 0.2f;               // operator tunes one clip afterwards

            Assert.False(ConfigMigration.SeedClipLooks(config));   // second run

            Assert.Equal(0.2f, config.Clips[0].Brightness);  // untouched
            Assert.Equal(0.6f, config.Clips[1].Brightness);  // untouched
        }

        [Fact]
        public void ANeutralGlobalSeedsNothing()
        {
            var config = ConfigWith(ClipEntry.NeutralLook, ClipEntry.NeutralLook, 1);

            Assert.False(ConfigMigration.SeedClipLooks(config));
            Assert.Equal(ClipEntry.NeutralLook, config.Clips[0].Brightness);
        }

        /// <summary>
        /// config.json is deliberately not range-validated, so a NaN global would otherwise read as
        /// "non-neutral" and be written onto every clip. Through the clamp, NaN is the neutral it
        /// displays as -- no clip is seeded a NaN.
        /// </summary>
        [Fact]
        public void ANaNGlobalIsTreatedAsNeutralAndSeedsNothing()
        {
            var config = ConfigWith(float.NaN, float.NaN, 1);

            Assert.False(ConfigMigration.SeedClipLooks(config));
            Assert.Equal(ClipEntry.NeutralLook, config.Clips[0].Brightness);
            Assert.Equal(ClipEntry.NeutralLook, config.Clips[0].Contrast);
        }

        /// <summary>
        /// An out-of-range global clamps to the range end when seeded, never stores infinity. A
        /// config.json holding an ordinary-looking 1e40 overflows float to infinity on parse, which
        /// is the real path here (the literal 1e40f won't compile, but the parsed value gets here).
        /// </summary>
        [Fact]
        public void AnOverflowGlobalIsClampedWhenSeeded()
        {
            var config = ConfigWith(float.PositiveInfinity, ClipEntry.NeutralLook, 1);

            Assert.True(ConfigMigration.SeedClipLooks(config));
            Assert.Equal(AdjustValue.Max, config.Clips[0].Brightness);
        }
    }
}
