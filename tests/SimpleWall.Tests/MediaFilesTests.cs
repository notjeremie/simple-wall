using SimpleWall.Model;
using Xunit;

namespace SimpleWall.Tests
{
    public class MediaFilesTests
    {
        [Theory]
        [InlineData(@"C:\wall\a.mp4")]
        [InlineData(@"C:\wall\a.MOV")]
        [InlineData(@"V:\gfx\b.mkv")]
        public void VideosAreVideos(string path)
        {
            Assert.True(MediaFiles.IsVideo(path));
            Assert.False(MediaFiles.IsImage(path));
            Assert.True(MediaFiles.IsSupported(path));
        }

        [Theory]
        [InlineData(@"C:\wall\a.png")]
        [InlineData(@"C:\wall\a.PNG")]
        [InlineData(@"C:\wall\a.jpg")]
        [InlineData(@"C:\wall\a.jpeg")]
        [InlineData(@"C:\wall\a.bmp")]
        public void ImagesAreImages(string path)
        {
            Assert.True(MediaFiles.IsImage(path));
            Assert.False(MediaFiles.IsVideo(path));
            Assert.True(MediaFiles.IsSupported(path));
        }

        /// <summary>
        /// GIF is out on purpose: an animated GIF is neither a still nor a normal clip, and how
        /// libvlc plays one on this wall has never been measured. Add it when it has.
        /// </summary>
        [Theory]
        [InlineData(@"C:\wall\a.gif")]
        [InlineData(@"C:\wall\notes.txt")]
        [InlineData(@"C:\wall\png")]
        [InlineData("")]
        [InlineData(null)]
        public void EverythingElseIsRejected(string path)
        {
            Assert.False(MediaFiles.IsSupported(path));
        }

        [Fact]
        public void TheDialogOffersBothKinds()
        {
            var filter = MediaFiles.DialogFilter;
            Assert.Contains("*.mp4", filter);
            Assert.Contains("*.png", filter);

            // WinForms throws on an odd number of '|'-separated parts -- at dialog open, on the wall.
            Assert.Equal(0, filter.Split('|').Length % 2);
        }
    }
}
