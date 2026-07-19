using System.Collections.Generic;
using SimpleWall.Model;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The boot-clip decision, extracted from MainForm.OnShown so it can be tested without a
    /// window. This is what puts a picture on an unattended wall after a power-cut reboot instead
    /// of leaving it black until the next scheduled task. The load-bearing rule: a default that
    /// points at a removed or missing clip is a DARK boot, never a fall-back to some other clip --
    /// an unexpected clip on the wall is worse than a dark one.
    /// </summary>
    public class MainFormBootClipTests
    {
        private static ClipLibrary LibraryWith(params int[] slots)
        {
            var clips = new List<ClipEntry>();
            foreach (var slot in slots)
                clips.Add(new ClipEntry { Slot = slot, Path = $@"V:\clips\{slot}.mp4" });
            return new ClipLibrary(clips);
        }

        [Fact]
        public void NoDefaultSetIsADarkBoot()
        {
            Assert.Null(MainForm.DefaultClipToPlay(
                new WallConfig { DefaultSlot = 0 }, LibraryWith(1, 2), _ => true));
        }

        [Fact]
        public void DefaultSetAndPresentAndFilePresentBootsIntoIt()
        {
            Assert.Equal(2, MainForm.DefaultClipToPlay(
                new WallConfig { DefaultSlot = 2 }, LibraryWith(1, 2), _ => true));
        }

        [Fact]
        public void DefaultPointingAtARemovedSlotIsADarkBootNotAFallback()
        {
            // Slot 2's clip was removed (library holds 1 and 3). Must NOT boot into 1 or 3.
            Assert.Null(MainForm.DefaultClipToPlay(
                new WallConfig { DefaultSlot = 2 }, LibraryWith(1, 3), _ => true));
        }

        [Fact]
        public void DefaultWhoseFileWentMissingIsADarkBoot()
        {
            Assert.Null(MainForm.DefaultClipToPlay(
                new WallConfig { DefaultSlot = 1 }, LibraryWith(1), _ => false));
        }
    }
}
