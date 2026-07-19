using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Drives real libvlc against a real clip. The fixture (tests/fixtures) is 1964x256 to match
    /// the wall's actual clips, and is deliberately RED for its first second and BLUE for its
    /// second: asserting "a PNG appeared" would pass just as happily on a black frame VLC hadn't
    /// decoded yet, which is exactly the failure worth catching.
    /// </summary>
    [Collection("LibVlc")]
    public class ThumbnailCacheTests : IDisposable
    {
        private static readonly string Fixture =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "red-then-blue-1964x256.mp4");

        private readonly string _dir;
        private readonly ThumbnailCache _cache;

        public ThumbnailCacheTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sw-thumbs-" + Guid.NewGuid().ToString("N"));
            _cache = new ThumbnailCache(_dir);
        }

        public void Dispose()
        {
            _cache.Dispose();
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        [Fact]
        public void TheFixtureIsWhereTheTestsThinkItIs()
        {
            Assert.True(File.Exists(Fixture),
                $"fixture not copied to the test output: {Fixture}. Without it every test below " +
                "would pass vacuously by hitting the 'clip missing' path.");
        }

        [Fact]
        public async Task ExtractsTheActualFirstFrameNotABlackOne()
        {
            var png = await _cache.GetAsync(Fixture);

            Assert.NotNull(png);
            Assert.True(File.Exists(png));

            using (var bitmap = new Bitmap(png))
            {
                Assert.Equal(ThumbnailCache.Width, bitmap.Width);
                Assert.Equal(ThumbnailCache.Height, bitmap.Height);

                var centre = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                Assert.True(centre.R > 200 && centre.G < 60 && centre.B < 60,
                    $"expected the clip's red first frame, got {centre}. A black or blue frame " +
                    "means we grabbed the wrong moment, not that extraction failed.");
            }
        }

        [Fact]
        public async Task SecondCallIsServedFromDiskWithoutReExtracting()
        {
            var first = await _cache.GetAsync(Fixture);
            var stamp = File.GetLastWriteTimeUtc(first);

            var second = await _cache.GetAsync(Fixture);

            Assert.Equal(first, second);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(second));
            Assert.Equal(first, _cache.TryGet(Fixture));
        }

        [Fact]
        public void TryGetIsNullBeforeAnythingIsExtracted()
        {
            Assert.Null(_cache.TryGet(Fixture));
        }

        /// <summary>
        /// Replacing a clip with a different one at the same path must re-thumbnail. Otherwise the
        /// grid keeps showing the old picture and lies about what is on the wall.
        /// </summary>
        [Fact]
        public void ReplacingTheFileOnDiskChangesTheCacheKey()
        {
            var copy = Path.Combine(_dir, "clip.mp4");
            Directory.CreateDirectory(_dir);
            File.Copy(Fixture, copy, true);

            var before = _cache.CachePathFor(copy);
            File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(5));
            var after = _cache.CachePathFor(copy);

            Assert.NotEqual(before, after);
        }

        [Fact]
        public async Task AMissingClipIsNullRatherThanAnException()
        {
            Assert.Null(await _cache.GetAsync(Path.Combine(_dir, "does-not-exist.mp4")));
            Assert.Null(await _cache.GetAsync(null));
            Assert.Null(await _cache.GetAsync(""));
        }

        /// <summary>
        /// A request arriving after Dispose must not quietly build a fresh LibVLC that nothing
        /// will ever release. MainForm's thumbnail loop can still be running when Program.Main
        /// disposes the cache on the way out, so this is the ordinary shutdown path, not an
        /// exotic one.
        /// </summary>
        [Fact]
        public async Task AfterDisposeItRefusesRatherThanBuildingAnotherLibVlc()
        {
            var cache = new ThumbnailCache(Path.Combine(_dir, "disposed"));
            cache.Dispose();

            Assert.Null(await cache.GetAsync(Fixture));
        }

        /// <summary>
        /// An unreadable file must not throw: an operator can drag anything onto this grid.
        /// </summary>
        [Fact]
        public async Task GarbageIsNullRatherThanAnException()
        {
            Directory.CreateDirectory(_dir);
            var junk = Path.Combine(_dir, "not-really-a-clip.mp4");
            File.WriteAllText(junk, "this is not an mp4");

            Assert.Null(await _cache.GetAsync(junk));
        }
    }
}
