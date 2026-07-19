using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace SimpleWall.UI
{
    /// <summary>
    /// First frame of each clip, extracted once and cached to disk.
    ///
    /// The thumbnail is the point, not decoration: the real clips are named things like
    /// INNONATION_WALL_3.mp4 and WALL_BEFORE_SUNSET_1964X256.mp4, and a filename does not tell
    /// an operator what is about to go on the wall.
    ///
    /// Two rules shape everything here:
    ///
    ///   1. **Never block startup.** Extraction is async and the grid draws immediately without
    ///      it. A wall that takes ten seconds to open because it is decoding fifty first-frames
    ///      is a worse wall.
    ///   2. **Never touch a video card.** Extraction runs on its OWN LibVLC instance with
    ///      --vout=dummy and the scene video filter, so no vout is ever created. The wall's
    ///      engine is mid-playback on a Radeon HD 7800 that already tears down and rebuilds its
    ///      vout on every clip change; fifty thumbnail vouts fighting it for the GPU is not a
    ///      risk worth taking for a picture of a first frame.
    ///
    /// The separate instance is forced, not chosen: --vout=dummy and the scene-* options are
    /// instance-level and read once at construction, and they are the exact opposite of what
    /// the wall's instance needs.
    /// </summary>
    public class ThumbnailCache : IDisposable
    {
        public const int Width = 160;
        public const int Height = 90;

        /// <summary>
        /// Enough of the clip to be sure a frame came out, and no more. At 25fps this decodes
        /// ~5 frames and the scene filter writes each one; we keep the first and bin the rest.
        /// Taking the first of a few is more robust than trying to make libvlc emit exactly one
        /// across every frame rate we might be handed.
        /// </summary>
        private const string StopTime = "0.2";

        private static readonly TimeSpan ExtractTimeout = TimeSpan.FromSeconds(20);

        private readonly string _directory;
        private readonly string _stagingDirectory;

        // One at a time: --scene-path is an instance-level option, so every extraction writes
        // into the SAME staging directory. Two at once would read each other's frames.
        private readonly SemaphoreSlim _oneAtATime = new SemaphoreSlim(1, 1);
        private readonly object _instanceGate = new object();

        private LibVLC _libVlc;
        private bool _disposed;

        public ThumbnailCache(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "simple-wall", "thumbs");

            // Staging is PER INSTANCE, never a shared well-known path. Two instances of this app
            // can exist at once (autostart racing a manual launch is a known open thread), and
            // they would otherwise write frames into the same directory: at best one deletes the
            // other's frames and gets no thumbnail, at worst it picks up the OTHER clip's frame
            // and caches it under this clip's hash -- a wrong picture, permanently, because the
            // path+mtime key means it never re-extracts. That is exactly the lie the key exists
            // to prevent.
            _stagingDirectory = Path.Combine(
                Path.GetTempPath(), "simple-wall-thumb-staging", Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Where this clip's thumbnail lives. Keyed by path AND last-write-time, so replacing a
        /// clip on disk with a new one at the same path re-thumbnails it instead of showing the
        /// old picture forever -- which would be a lie about what is on the wall.
        /// </summary>
        public string CachePathFor(string clipPath)
        {
            if (string.IsNullOrEmpty(clipPath)) return null;

            var stamp = File.Exists(clipPath) ? File.GetLastWriteTimeUtc(clipPath).Ticks : 0L;
            var key = clipPath.ToLowerInvariant() + "|" + stamp.ToString();

            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var name = string.Concat(hash.Select(b => b.ToString("x2")));
                return Path.Combine(_directory, name + ".png");
            }
        }

        /// <summary>The cached thumbnail, or null if it hasn't been made yet. Never extracts.</summary>
        public string TryGet(string clipPath)
        {
            var path = CachePathFor(clipPath);
            return path != null && File.Exists(path) ? path : null;
        }

        /// <summary>
        /// The cached thumbnail, extracting it first if needed. Returns null rather than throwing
        /// when the clip is missing or unreadable: a thumbnail is a nicety, and nothing about the
        /// grid or the wall should fall over because a picture couldn't be made.
        /// </summary>
        public async Task<string> GetAsync(string clipPath)
        {
            if (string.IsNullOrWhiteSpace(clipPath) || !File.Exists(clipPath)) return null;

            var destination = CachePathFor(clipPath);
            if (File.Exists(destination)) return destination;

            await _oneAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another caller may have made it while we queued.
                if (File.Exists(destination)) return destination;
                return await Task.Run(() => Extract(clipPath, destination)).ConfigureAwait(false);
            }
            finally
            {
                _oneAtATime.Release();
            }
        }

        private string Extract(string clipPath, string destination)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                ResetStaging();

                using (var player = new MediaPlayer(Instance()))
                using (var media = new Media(Instance(), clipPath, FromType.FromPath))
                using (var finished = new ManualResetEventSlim(false))
                {
                    media.AddOption(":stop-time=" + StopTime);

                    // Belt and braces with --avcodec-hw=none on the instance (see LibVlcOptions):
                    // a media-level avcodec-hw IS honoured -- the wall proves it with
                    // :avcodec-hw=dxva2 -- so even if the instance option were ever "simplified"
                    // away, this keeps the DXVA2 plugin (libdxva2_plugin.dll, which 0xc0000005'd
                    // on the real Radeon) out of the extraction path.
                    media.AddOption(":avcodec-hw=none");

                    player.EndReached += (s, e) => finished.Set();
                    player.EncounteredError += (s, e) => finished.Set();

                    player.Play(media);
                    finished.Wait(ExtractTimeout);
                    player.Stop();
                }

                var frame = Directory.EnumerateFiles(_stagingDirectory, "*.png").OrderBy(f => f).FirstOrDefault();
                if (frame == null) return null;

                File.Copy(frame, destination, true);
                return destination;
            }
            catch (Exception)
            {
                // A thumbnail is never worth taking anything else down with it.
                return null;
            }
            finally
            {
                try { ResetStaging(); } catch { /* best effort */ }
            }
        }

        private void TryDeleteStaging()
        {
            try
            {
                if (Directory.Exists(_stagingDirectory)) Directory.Delete(_stagingDirectory, true);
            }
            catch
            {
                // It's under %TEMP% and uniquely named -- Windows can have it.
            }
        }

        private void ResetStaging()
        {
            if (Directory.Exists(_stagingDirectory))
                foreach (var file in Directory.EnumerateFiles(_stagingDirectory, "*.png"))
                    try { File.Delete(file); } catch { /* it'll be overwritten anyway */ }
            else
                Directory.CreateDirectory(_stagingDirectory);
        }

        /// <summary>
        /// The instance options for the extraction-only LibVLC. Exposed and static so the fact
        /// that matters most here can be pinned by a test rather than trusted to a comment:
        /// <c>--avcodec-hw=none</c> MUST be present.
        ///
        /// --vout=dummy stops libvlc RENDERING on the GPU; it does NOT stop it DECODING on the
        /// GPU. Without --avcodec-hw=none, on the real Win7 wall PC (AMD Radeon HD 7800) libvlc
        /// loaded its DXVA2 hardware decoder and access-violated inside libdxva2_plugin.dll
        /// (0xc0000005, offset 0x1cf3) ~0.5s after any clip was added -- a native corrupted-state
        /// exception the managed handler never sees, so the wall died leaving no log line. The
        /// GPU-less build VM software-decoded and never reproduced it, which is exactly why this
        /// went unseen until the wall. Thumbnails are 160x90 and decode ~5 frames: software
        /// decode is trivial and correct. The wall's OWN engine keeps DXVA2 -- it has a real
        /// Direct3D vout to receive the hardware surfaces; this instance, with a dummy vout and
        /// a CPU-side scene filter, must never hand GPU surfaces anywhere.
        ///
        /// The --video-filter=scene and --scene-* options MUST be instance-level. As per-media
        /// options they are silently ignored -- the filter chain is never built and not one
        /// frame comes out, with no error anywhere. Measured the hard way.
        /// </summary>
        public static string[] LibVlcOptions(string stagingDirectory) => new[]
        {
            "--no-audio",
            "--vout=dummy",
            "--avcodec-hw=none",
            "--verbose=0",
            "--video-filter=scene",
            "--scene-format=png",
            "--scene-prefix=t",
            "--scene-path=" + stagingDirectory,
            "--scene-ratio=1",
            "--scene-width=" + Width,
            "--scene-height=" + Height
        };

        /// <summary>
        /// Built on first use, on whatever background thread got here first, because constructing
        /// a LibVLC rescans every bundled plugin (this package ships no plugin cache) and that can
        /// take seconds on a slow disk. Doing it in the constructor would put those seconds
        /// straight into startup, which is the one thing this class must not do.
        /// </summary>
        private LibVLC Instance()
        {
            lock (_instanceGate)
            {
                // Without this, a call arriving after Dispose builds a BRAND NEW LibVLC that
                // nothing will ever release -- libvlc and its decoder threads for the life of
                // the process, through the back door.
                if (_disposed) throw new ObjectDisposedException(nameof(ThumbnailCache));
                if (_libVlc != null) return _libVlc;

                Directory.CreateDirectory(_stagingDirectory);
                Core.Initialize();

                _libVlc = new LibVLC(LibVlcOptions(_stagingDirectory));

                return _libVlc;
            }
        }

        /// <summary>
        /// Waits for any extraction already in flight before releasing libvlc.
        ///
        /// Disposing LibVLC while a MediaPlayer built from it is still playing is
        /// libvlc_release against a live player: a 0xC0000005 access violation, reproduced 3/3.
        /// And since .NET 4.0 a corrupted-state exception is NOT delivered to
        /// AppDomain.UnhandledException, so Program's crash handler writes nothing -- the wall
        /// PC would die leaving exactly no evidence, which is the one thing it must never do.
        /// It is not a narrow window either: extractions are serialised and take ~1-3s each, so
        /// 50 clips means ~100s of exposure on every launch, and closing the window lands in it.
        ///
        /// If the drain times out we leave libvlc alone and let the process reclaim it. A leak
        /// on the way out costs nothing; an access violation costs the evidence.
        /// </summary>
        public void Dispose()
        {
            lock (_instanceGate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            var drained = _oneAtATime.Wait(ExtractTimeout + TimeSpan.FromSeconds(5));

            lock (_instanceGate)
            {
                if (drained)
                {
                    _libVlc?.Dispose();
                    _libVlc = null;
                }
            }

            if (drained) _oneAtATime.Release();
            TryDeleteStaging();
        }
    }
}
