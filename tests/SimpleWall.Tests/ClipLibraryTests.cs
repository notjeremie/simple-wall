using System.Collections.Generic;
using System.Linq;
using Xunit;
using SimpleWall.Model;

namespace SimpleWall.Tests
{
    public class ClipLibraryTests
    {
        [Fact]
        public void FirstClipGetsSlotOne()
        {
            var library = new ClipLibrary();
            Assert.Equal(1, library.Add(@"C:\a.mp4").Slot);
        }

        [Fact]
        public void SlotsAreSequentialForSuccessiveAdds()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            Assert.Equal(3, library.Add(@"C:\c.mp4").Slot);
        }

        [Fact]
        public void RemovingAClipDoesNotRenumberTheOthers()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            library.Add(@"C:\c.mp4");

            library.Remove(2);

            Assert.Equal(new[] { 1, 3 }, library.Clips.Select(c => c.Slot));
            Assert.Equal(@"C:\c.mp4", library.BySlot(3).Path);
        }

        [Fact]
        public void NewClipReusesTheLowestFreeSlot()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");
            library.Add(@"C:\b.mp4");
            library.Add(@"C:\c.mp4");
            library.Remove(2);

            Assert.Equal(2, library.Add(@"C:\d.mp4").Slot);
        }

        [Fact]
        public void AddIsRefusedAtTheFiftyClipCeiling()
        {
            var library = new ClipLibrary();
            for (var i = 0; i < 50; i++) library.Add($@"C:\{i}.mp4");

            Assert.Null(library.Add(@"C:\overflow.mp4"));
            Assert.Equal(50, library.Clips.Count);
        }

        [Fact]
        public void BySlotReturnsNullForUnknownSlot()
        {
            Assert.Null(new ClipLibrary().BySlot(7));
        }

        [Fact]
        public void ConstructingFromExistingListPreservesSlotNumbers()
        {
            // This is the real-world path: config comes off disk with its own slot numbers,
            // and the library wraps it without renumbering anything.
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 4, Path = @"C:\a.mp4" },
                new ClipEntry { Slot = 2, Path = @"C:\b.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.Equal(new[] { 4, 2 }, library.Clips.Select(c => c.Slot));
        }

        [Fact]
        public void LibraryLoadedWithGapInSlotsFillsTheGapOnAdd()
        {
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 1, Path = @"C:\a.mp4" },
                new ClipEntry { Slot = 5, Path = @"C:\b.mp4" },
                new ClipEntry { Slot = 9, Path = @"C:\c.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.Equal(2, library.Add(@"C:\d.mp4").Slot);
            Assert.NotNull(library.BySlot(5));
            Assert.NotNull(library.BySlot(9));
        }

        [Fact]
        public void RemovingAnUnknownSlotIsAHarmlessNoOp()
        {
            var library = new ClipLibrary();
            library.Add(@"C:\a.mp4");

            library.Remove(99);

            Assert.Single(library.Clips);
            Assert.Equal(1, library.Clips[0].Slot);
        }
    }
}
