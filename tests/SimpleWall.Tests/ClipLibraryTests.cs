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

        [Fact]
        public void ConstructingWithDuplicateSlotsReassignsAllButTheFirstClaimant()
        {
            // A hand-edited or buggy config can hand us two entries both claiming slot 7.
            // ConfigStore deliberately doesn't validate this - it's this constructor's job.
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 7, Path = @"C:\first.mp4" },
                new ClipEntry { Slot = 7, Path = @"C:\second.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.True(library.WasNormalized);
            Assert.Equal(2, library.Clips.Count);
            // The first claimant keeps 7 - the same one BySlot's FirstOrDefault would have
            // picked anyway.
            Assert.Equal(@"C:\first.mp4", library.BySlot(7).Path);
            // The second clip is not dropped, just moved off of 7, to a slot nothing else holds.
            var slots = library.Clips.Select(c => c.Slot).ToList();
            Assert.Equal(slots.Count, slots.Distinct().Count());
            Assert.Contains(library.Clips, c => c.Path == @"C:\second.mp4" && c.Slot != 7);
        }

        [Fact]
        public void RemovingADuplicatedSlotDoesNotDeleteTheOtherClip_NoStreamDeckDrift()
        {
            // This is the exact failure the class exists to prevent: before normalization,
            // Remove(7) used RemoveAll(c => c.Slot == 7), which - handed a config with two
            // raw entries both at slot 7 - would delete BOTH, including one the operator
            // never selected. Normalizing at construction means Remove/Add never see a
            // duplicate slot in the first place.
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 7, Path = @"C:\first-seven.mp4" },
                new ClipEntry { Slot = 7, Path = @"C:\second-seven.mp4" },
            };

            var library = new ClipLibrary(fromConfig);
            var survivor = library.Clips.Single(c => c.Path == @"C:\second-seven.mp4");
            var survivorSlot = survivor.Slot;

            library.Remove(7);

            // The clip that was reassigned off of slot 7 during normalization must still be
            // there, at its own stable slot, completely unaffected by removing slot 7.
            Assert.NotNull(library.BySlot(survivorSlot));
            Assert.Equal(@"C:\second-seven.mp4", library.BySlot(survivorSlot).Path);

            var added = library.Add(@"C:\new.mp4");

            // A new clip must never be able to land on the surviving clip's slot - that
            // would be the same Stream Deck drift under a different slot number.
            Assert.NotEqual(survivorSlot, added.Slot);
        }

        [Fact]
        public void OutOfRangeSlotsFromConfigAreReassignedIntoValidRange()
        {
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 0, Path = @"C:\zero.mp4" },
                new ClipEntry { Slot = -3, Path = @"C:\negative.mp4" },
                new ClipEntry { Slot = 51, Path = @"C:\toohigh.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.True(library.WasNormalized);
            Assert.Equal(3, library.Clips.Count);
            Assert.All(library.Clips, c => Assert.InRange(c.Slot, 1, ClipLibrary.MaxClips));
        }

        [Fact]
        public void ConstructingFromAlreadyCleanConfigDoesNotNormalizeOrChangeSlots()
        {
            // Guards against the normalizer "helpfully" renumbering a healthy config with
            // gaps - which would itself be the drift this class exists to prevent.
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 1, Path = @"C:\a.mp4" },
                new ClipEntry { Slot = 5, Path = @"C:\b.mp4" },
                new ClipEntry { Slot = 9, Path = @"C:\c.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.False(library.WasNormalized);
            Assert.Equal(new[] { 1, 5, 9 }, library.Clips.Select(c => c.Slot));
        }

        [Fact]
        public void MoreThanFiftyEntriesFromConfigAreTruncatedToFifty()
        {
            var fromConfig = Enumerable.Range(1, 60)
                .Select(i => new ClipEntry { Slot = i, Path = $@"C:\{i}.mp4" })
                .ToList();

            var library = new ClipLibrary(fromConfig);

            Assert.True(library.WasNormalized);
            Assert.Equal(50, library.Clips.Count);
            Assert.Equal(@"C:\1.mp4", library.BySlot(1).Path);
            Assert.Null(library.BySlot(60));
        }

        [Fact]
        public void NormalizationMutatesTheSameListInstancePassedIn()
        {
            // So that a subsequent config save (the caller's List<ClipEntry>, unchanged
            // reference) persists the repair rather than silently discarding it.
            var fromConfig = new List<ClipEntry>
            {
                new ClipEntry { Slot = 7, Path = @"C:\a.mp4" },
                new ClipEntry { Slot = 7, Path = @"C:\b.mp4" },
            };

            var library = new ClipLibrary(fromConfig);

            Assert.Same(fromConfig, library.Clips);
            Assert.Equal(7, fromConfig[0].Slot);
            Assert.NotEqual(7, fromConfig[1].Slot);
        }

        /// <summary>
        /// The Task 4 contract, now reachable from a mouse drag: rearranging the grid must not
        /// re-point a Stream Deck button that was programmed months ago.
        /// </summary>
        [Fact]
        public void MovingAClipReordersWithoutRenumbering()
        {
            var library = new ClipLibrary();
            library.Add("a.mp4");   // slot 1
            library.Add("b.mp4");   // slot 2
            library.Add("c.mp4");   // slot 3

            library.Move(0, 2);     // drag "a" to the end

            Assert.Equal(new[] { "b.mp4", "c.mp4", "a.mp4" }, library.Clips.Select(c => c.Path).ToArray());
            Assert.Equal(new[] { 2, 3, 1 }, library.Clips.Select(c => c.Slot).ToArray());
            Assert.Equal("a.mp4", library.BySlot(1).Path);
        }

        [Fact]
        public void MovingWithASillyIndexIsANoOpNotACrash()
        {
            var library = new ClipLibrary();
            library.Add("a.mp4");
            library.Add("b.mp4");

            library.Move(-1, 0);
            library.Move(0, 99);
            library.Move(0, 0);

            Assert.Equal(new[] { "a.mp4", "b.mp4" }, library.Clips.Select(c => c.Path).ToArray());
        }

        /// <summary>
        /// "Same button, new video": replacing a slot's file keeps the slot NUMBER, so a Stream
        /// Deck "/clip/2" that was programmed months ago still triggers the right button.
        /// </summary>
        [Fact]
        public void ReplaceSwapsTheFileButKeepsTheSlotNumber()
        {
            var library = new ClipLibrary();
            library.Add("old-a.mp4");   // slot 1
            library.Add("old-b.mp4");   // slot 2

            Assert.True(library.Replace(2, "new-b.mp4"));

            Assert.Equal("new-b.mp4", library.BySlot(2).Path);
            Assert.Equal("old-a.mp4", library.BySlot(1).Path);   // untouched
            Assert.Equal(new[] { 1, 2 }, library.Clips.Select(c => c.Slot).ToArray());
            Assert.Equal(2, library.Clips.Count);                // replaced, not added
        }

        [Fact]
        public void ReplacingAnEmptySlotReturnsFalseAndChangesNothing()
        {
            var library = new ClipLibrary();
            library.Add("a.mp4");   // slot 1

            Assert.False(library.Replace(5, "nope.mp4"));
            Assert.Single(library.Clips);
            Assert.Null(library.BySlot(5));
        }
    }
}
