using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using SimpleWall.Model;
using SimpleWall.UI;

namespace SimpleWall.Engine
{
    /// <summary>
    /// Where tested logic meets the outside world. Deliberately thin: everything worth
    /// unit-testing already lives in ClipLibrary, GeometryValidator, OscParser and Scheduler,
    /// and the facts this depends on about libvlc itself are pinned by LibVlcContractTests.
    /// What is left here is orchestration, verified by hand on the wall in Task 15.
    ///
    /// Two things here are not obvious and are not negotiable, both measured on the real
    /// machine (see docs/plans/2026-07-16-spike-findings.md):
    ///
    ///   1. Clips change by loading the incoming one on a hidden back layer and flipping the
    ///      z-order once it actually has a picture. The naive "stop, then play" shows ~290ms
    ///      of black, which is plainly visible on the wall. See OutputWindow.
    ///
    ///   2. :input-repeat is a countdown, not "forever". At its 65535 maximum a 30s clip
    ///      stops after ~22 days, and this app runs unattended for months, so EndReached
    ///      restarts the clip. That is not a belt-and-braces nicety: without it the wall
    ///      goes black three weeks in and stays black.
    ///
    /// Threading: everything public here runs on the UI thread (see IWallEngine). libvlc
    /// raises its events on its OWN threads, and re-entering libvlc from inside one of its
    /// callbacks is a documented deadlock, so every handler here does nothing but marshal to
    /// the UI thread.
    /// </summary>
    public class VlcWallEngine : IWallEngine, IDisposable
    {
        /// <summary>
        /// How long to wait for the incoming layer to report a picture before swapping to it
        /// anyway. Measured first-picture on the real machine was ~286ms, so this is already
        /// generous; a dead second between press and change is a long time to stand there.
        ///
        /// Waiting is only ever an OPTIMISATION -- see <see cref="OnFirstPictureTick"/> for why
        /// running out of patience is not a failure.
        /// </summary>
        private static readonly TimeSpan FirstPictureTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// A WinForms timer floors at ~15.6ms regardless of what is asked for, so this is
        /// "as fast as the UI thread will do", not a precise 15ms. Polling VoutCount is how
        /// the spike measured first-picture; the Playing event fires ~170ms too early
        /// (112ms vs 286ms), and swapping on it would show exactly the black frame the two
        /// layers exist to hide.
        /// </summary>
        private const int PollIntervalMs = 15;

        /// <summary>
        /// EXPERIMENT (2026-07-21): once the incoming layer reports a vout, hold the OUTGOING
        /// clip in front for this long before flipping, to let the occluded incoming layer
        /// actually paint its first frame while it is still hidden. The open question this
        /// answers: does libvlc present frames to an occluded window? If yes, the visible black
        /// at a cut should shrink or vanish. If the black is unchanged and only the switch feels
        /// slower, occluded layers do NOT paint until fronted, and the two-layer trick cannot be
        /// made seamless on this hardware. Instrumented via _firstVoutAt logging below.
        /// </summary>
        private const int SwapSettleMs = 500;

        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _playerA;
        private readonly MediaPlayer _playerB;
        private readonly OutputWindow _outputWindow;
        private readonly ClipLibrary _library;
        private readonly WallConfig _config;
        private readonly Timer _firstPictureTimer;
        private readonly Stopwatch _loadStopwatch = new Stopwatch();
        private readonly Action<string> _log;

        // EXPERIMENT: elapsed time at which the pending clip first reported a vout, or null if
        // not yet. The settle window (SwapSettleMs) is measured from here. Reset per load.
        private TimeSpan? _firstVoutAt;

        private bool _frontIsA = true;
        private int? _pendingSlot;
        private Media _frontMedia;
        private Media _pendingMedia;

        /// <summary>
        /// Set from libvlc's thread when the pending clip errors; read by the poll on the UI
        /// thread. It is the ONLY thing separating "unplayable, abandon it" from "playing fine
        /// but hasn't reported a picture yet, swap anyway" -- without it, swapping on timeout
        /// would blank the wall for a corrupt file.
        /// </summary>
        private volatile bool _pendingFailed;

        private bool _disposed;

        public VlcWallEngine(ClipLibrary library, WallConfig config, Action<string> log = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? (_ => { });

            try
            {
                Core.Initialize();
                _libVlc = new LibVLC(VlcOptions.LibVlc());
                _log($"LibVLC {_libVlc.Version}");

                _playerA = new MediaPlayer(_libVlc);
                _playerB = new MediaPlayer(_libVlc);
                _playerA.EndReached += (s, e) => OnEndReached(true);
                _playerB.EndReached += (s, e) => OnEndReached(false);

                // Nothing but a flag: this runs on libvlc's thread, and re-entering libvlc from
                // inside its own callback is a documented deadlock. The poll reads it.
                _playerA.EncounteredError += (s, e) => OnEncounteredError(true);
                _playerB.EncounteredError += (s, e) => OnEncounteredError(false);

                _outputWindow = new OutputWindow(_playerA, _playerB, _log);
                ApplyGeometry();

                // No clip is loaded yet, so both layers start neutral. Each clip's own look is
                // applied to the back layer as it loads (StartLoad), before it swaps in.
                ApplyAdjust(_playerA, null);
                ApplyAdjust(_playerB, null);

                _firstPictureTimer = new Timer { Interval = PollIntervalMs };
                _firstPictureTimer.Tick += OnFirstPictureTick;
            }
            catch
            {
                // A throw here leaves the caller with no reference, so nothing else can ever
                // dispose what was already built -- libvlc and its decoder threads would sit
                // there for the life of the process. Everything above can throw: ApplyGeometry
                // reads real screens, and libvlc construction is the exact thing that returned
                // NULL once already.
                TearDown();
                throw;
            }
        }

        public int? CurrentSlot { get; private set; }

        public bool IsPlaying => CurrentSlot != null && FrontPlayer.IsPlaying;

        // Clamped, because the clip's look came from config.json (not range-validated) and a reader
        // like the OSC reply must not send a NaN/overflow out onto the network.
        public float CurrentBrightness => ClampAdjust(CurrentLookClip?.Brightness ?? AdjustValue.Neutral);
        public float CurrentContrast => ClampAdjust(CurrentLookClip?.Contrast ?? AdjustValue.Neutral);

        public event EventHandler StateChanged;
        public event EventHandler<ClipUnavailableEventArgs> ClipUnavailable;

        private MediaPlayer FrontPlayer => _frontIsA ? _playerA : _playerB;
        private MediaPlayer BackPlayer => _frontIsA ? _playerB : _playerA;

        public void Execute(WallCommand command)
        {
            if (command == null) return;

            switch (command.Kind)
            {
                case CommandKind.PlayClip: PlayClip(command.Slot); break;
                case CommandKind.Play: SetPaused(false); break;
                case CommandKind.Pause: SetPaused(true); break;
                case CommandKind.Toggle: SetPaused(IsPlaying); break;
                case CommandKind.Stop: Stop(); break;
                case CommandKind.Brightness: SetBrightness(command.Value); break;
                case CommandKind.Contrast: SetContrast(command.Value); break;
            }
        }

        /// <summary>
        /// Repositions the output window from the saved geometry, resolved against the screens
        /// actually connected right now. Public so settings can move it live.
        ///
        /// Resolve, not Validate: on a first run the config holds zeros, and Validate would pass
        /// 0,0 straight through as a legitimate setting that happens to sit on the operator's
        /// desktop instead of the wall. See GeometryValidator.Resolve.
        /// </summary>
        public void ApplyGeometry()
        {
            var screens = Screen.AllScreens.Select(s => s.Bounds).ToArray();
            var primary = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
            var saved = new System.Drawing.Rectangle(
                _config.OutputX, _config.OutputY, _config.OutputWidth, _config.OutputHeight);

            var bounds = GeometryValidator.Resolve(saved, screens, primary);

            // Write the resolved geometry back, so a first run's choice of screen is what the
            // config says from now on rather than being re-derived every start.
            _config.OutputX = bounds.X;
            _config.OutputY = bounds.Y;
            _config.OutputWidth = bounds.Width;
            _config.OutputHeight = bounds.Height;

            _outputWindow.SetGeometry(bounds);

            // Re-crop to the (possibly new) wall ratio so cover-fit tracks a geometry change.
            // Both layers, so the idle back player is already correct before its next clip.
            ApplyCropToFill(_playerA);
            ApplyCropToFill(_playerB);

            _log($"Output geometry {bounds.Width}x{bounds.Height} @{bounds.X},{bounds.Y}");
        }

        private void PlayClip(int slot)
        {
            var entry = _library.BySlot(slot);
            if (entry == null)
            {
                RaiseUnavailable(slot, null, "no clip assigned to this slot");
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Path) || !File.Exists(entry.Path))
            {
                RaiseUnavailable(slot, entry.Path, "file not found");
                return;
            }

            // Already loading it -- a second press must not restart the load and lose the
            // ~290ms of progress it has already made.
            if (_pendingSlot == slot) return;

            // Already on the wall and running: ignore. Restarting would blank a clip that is
            // playing correctly, which is the worst possible answer to "play the thing already
            // playing" -- the Stream Deck sends this every time someone leans on a button.
            //
            // Note the deliberate asymmetry: if this slot is on the wall but PAUSED, the guard
            // does not fire and the clip reloads from the top. Pressing a clip button is a
            // request for that clip, not a resume -- /play and /toggle are how you resume.
            if (_pendingSlot == null && CurrentSlot == slot && FrontPlayer.IsPlaying) return;

            StartLoad(slot, entry.Path);
        }

        /// <summary>
        /// Loads a clip onto the back layer. Does NOT touch the wall: the outgoing clip keeps
        /// playing in front until the incoming layer has a picture (OnFirstPictureTick).
        /// </summary>
        private void StartLoad(int slot, string path)
        {
            CancelPending();

            var media = new Media(_libVlc, path, FromType.FromPath);
            foreach (var option in VlcOptions.Media())
                media.AddOption(option);

            _pendingSlot = slot;
            _pendingMedia = media;

            // Cleared before Play, never after: a stale true from the previous clip would
            // abandon this one on sight, and every clip after it.
            _pendingFailed = false;
            _firstVoutAt = null;

            // Shown BEFORE Play, never after: libvlc builds its vout against whatever window
            // it is handed at that moment. A window that is invisible at Play and shown
            // afterwards is untested territory on this GPU, and the failure mode is a black
            // wall, not an exception.
            if (!_outputWindow.Visible) _outputWindow.Show();

            var back = BackPlayer;
            back.Play(media);

            // After Play, matching the spike: the adjust filter is inserted into a live vout.
            // This clip's OWN look is applied to the back layer before it swaps in, so the cut
            // lands at the right brightness/contrast with no flash at the wrong value. BySlot can
            // return null only if the clip vanished between PlayClip's check and here, in which
            // case neutral is the safe thing to show.
            ApplyAdjust(back, _library.BySlot(slot));

            // Cover-fit the incoming clip before it swaps in, so a wrong-sized clip fills the
            // wall instead of letterboxing. Same after-Play timing as the adjust filter.
            ApplyCropToFill(back);

            _loadStopwatch.Restart();
            _firstPictureTimer.Start();
            _log($"Loading slot {slot} on back layer: {path}");
        }

        /// <summary>
        /// The swap. Polls until the incoming layer reports a video output -- the closest
        /// available proxy for "there is a frame on the wall" -- then flips z-order.
        ///
        /// Running out of patience is NOT a failure, and this distinction is the whole design:
        ///
        ///   * A clip that reported a picture swaps invisibly. That is the fast path and the
        ///     reason the two layers exist.
        ///   * A clip that is playing fine but hasn't reported a vout swaps ANYWAY. It may
        ///     simply be that nobody can see it: whether libvlc builds a Direct3D9 vout against
        ///     an occluded window is unproven (--vout=dummy never increments VoutCount, so no
        ///     test can reach it, and the build VM has no GPU). If that is what's happening, the
        ///     vout starts the instant the layer is brought to the front, and the worst case is
        ///     the ~290ms of black we would have had with no layers at all. These are looped
        ///     background clips -- nothing is frame-critical, and starting mid-loop costs
        ///     nothing. Degrading to the old visible cut beats a wall that stops changing.
        ///   * A clip libvlc has actually FAILED on is abandoned, because swapping to it would
        ///     blank the wall for real. That is what _pendingFailed is for.
        ///
        /// This used to abandon on timeout, which assumed no-vout meant a broken clip. It made
        /// an unproven assumption load-bearing: if occluded vouts don't start, every clip change
        /// would have waited 5s and then done nothing, leaving every button dead.
        /// </summary>
        private void OnFirstPictureTick(object sender, EventArgs e)
        {
            if (_pendingSlot == null)
            {
                _firstPictureTimer.Stop();
                return;
            }

            switch (SwapPolicy.Decide(_pendingFailed, BackPlayer.VoutCount, _loadStopwatch.Elapsed, FirstPictureTimeout))
            {
                case SwapAction.KeepWaiting:
                    return;

                case SwapAction.SwapNow:
                    // EXPERIMENT: the incoming layer has a vout, but a vout is not a painted
                    // frame. Hold the outgoing clip in front for SwapSettleMs to give the
                    // occluded incoming layer time to paint before the flip. The log below is
                    // the evidence: if the visible black shrinks, occluded layers do paint;
                    // if not, they only paint once fronted and this cannot be made seamless.
                    if (_firstVoutAt == null)
                    {
                        _firstVoutAt = _loadStopwatch.Elapsed;
                        _log($"Slot {_pendingSlot} first vout at {_firstVoutAt.Value.TotalMilliseconds:0}ms " +
                             $"(back Time={BackPlayer.Time}ms); settling {SwapSettleMs}ms before flip");
                        return;
                    }
                    if ((_loadStopwatch.Elapsed - _firstVoutAt.Value).TotalMilliseconds < SwapSettleMs)
                        return;

                    _log($"Slot {_pendingSlot} settle done (back Time={BackPlayer.Time}ms)");
                    CompleteSwap();
                    return;

                case SwapAction.SwapAnyway:
                    _log($"Slot {_pendingSlot} reported no picture within {FirstPictureTimeout.TotalMilliseconds}ms " +
                         "-- swapping anyway; expect a brief black frame.");
                    CompleteSwap();
                    return;

                case SwapAction.Abandon:
                    var slot = _pendingSlot.Value;
                    var path = _library.BySlot(slot)?.Path;
                    _log($"Slot {slot}: libvlc could not play it -- abandoning, wall unchanged.");
                    CancelPending();
                    RaiseUnavailable(slot, path, "the clip could not be played");
                    return;
            }
        }

        private void CompleteSwap()
        {
            _firstPictureTimer.Stop();

            var outgoing = FrontPlayer;
            var incomingIsA = !_frontIsA;

            _outputWindow.BringLayerToFront(incomingIsA);
            _frontIsA = incomingIsA;

            // Only now is the outgoing clip off screen, so stopping it cannot show black.
            outgoing.Stop();
            _frontMedia?.Dispose();
            _frontMedia = _pendingMedia;
            _pendingMedia = null;

            CurrentSlot = _pendingSlot;
            _pendingSlot = null;
            _pendingFailed = false;
            _firstVoutAt = null;

            _log($"Swapped to slot {CurrentSlot} after {_loadStopwatch.ElapsedMilliseconds} ms");
            RaiseStateChanged();
        }

        /// <summary>Abandons an in-flight load. The wall is left exactly as it was.</summary>
        private void CancelPending()
        {
            _firstPictureTimer.Stop();
            if (_pendingSlot == null && _pendingMedia == null) return;

            BackPlayer.Stop();
            _pendingMedia?.Dispose();
            _pendingMedia = null;
            _pendingSlot = null;
            _pendingFailed = false;
            _firstVoutAt = null;
        }

        private void SetPaused(bool paused)
        {
            // Nothing loaded: Play with no clip would be a no-op in libvlc anyway, but being
            // explicit keeps CurrentSlot/IsPlaying honest for the UI.
            if (CurrentSlot == null) return;

            // SetPause(bool), never Pause(): Pause() TOGGLES, so two OSC /pause messages in
            // a row would resume the wall. Toggle is spelled out at the call site instead.
            FrontPlayer.SetPause(paused);
            RaiseStateChanged();
        }

        private void Stop()
        {
            CancelPending();
            _playerA.Stop();
            _playerB.Stop();
            _frontMedia?.Dispose();
            _frontMedia = null;
            CurrentSlot = null;
            _log("Stopped.");
            RaiseStateChanged();
        }

        /// <summary>
        /// Brightness and contrast are a property of the CLIP now, not the wall: this edits the
        /// look of whatever is currently on the wall and applies it to the front layer only (the
        /// back layer, if loading, already holds the incoming clip's own look). RaiseStateChanged
        /// lets the UI persist the edit -- "what I see is what's saved".
        ///
        /// Nothing playing means there is no clip to edit, so the change is dropped, not stored
        /// against the wrong clip. Logged, because a fader that quietly does nothing reads as broken.
        /// </summary>
        private void SetBrightness(float value)
        {
            var clip = CurrentLookClip;
            if (clip == null) { _log("Brightness change ignored -- no clip on the wall."); return; }
            clip.Brightness = ClampAdjust(value);
            ApplyAdjust(FrontPlayer, clip);
            RaiseStateChanged();
        }

        private void SetContrast(float value)
        {
            var clip = CurrentLookClip;
            if (clip == null) { _log("Contrast change ignored -- no clip on the wall."); return; }
            clip.Contrast = ClampAdjust(value);
            ApplyAdjust(FrontPlayer, clip);
            RaiseStateChanged();
        }

        /// <summary>The clip whose look the fader/slider edits: the one on the wall, or null.</summary>
        private ClipEntry CurrentLookClip => CurrentSlot != null ? _library.BySlot(CurrentSlot.Value) : null;

        /// <summary>
        /// The engine clamps rather than trusting its callers, because it cannot see where a
        /// value came from and one route is already unguarded: config.json is deliberately NOT
        /// range-validated on load (a documented decision), so a hand-edited or half-corrupted
        /// "Brightness": 50 arrives here at startup and goes straight to a native SetAdjustFloat.
        /// OscParser clamps its own inputs, but IWallEngine is the single point of contact for
        /// the mouse and the scheduler too, so the clamp belongs on this side of the boundary.
        /// </summary>
        private static float ClampAdjust(float value) => AdjustValue.Clamp(value);

        /// <summary>
        /// Applies a clip's look to one player. A null clip means "no clip" -- neutral -- which is
        /// what the constructor uses before anything is loaded. Each layer carries the look of the
        /// clip IT holds: the front layer the clip on the wall, the back layer the incoming clip
        /// (set in StartLoad before the swap), so the cut never flashes at the wrong value.
        ///
        /// The mutation of the clip's stored look happens in SetBrightness/SetContrast, not here;
        /// this only pushes a value at libvlc and does NOT save. Persistence is the UI's job, on a
        /// debounce -- an OSC fader sweep is ~100 packets a second and a file write each is not a
        /// thing to do.
        /// </summary>
        private void ApplyAdjust(MediaPlayer player, ClipEntry clip)
        {
            // Clamped on read as well as on write: the values came from config.json (via the clip)
            // and nothing on that path has vetted them -- config is deliberately not range-validated.
            float brightness = clip != null ? clip.Brightness : AdjustValue.Neutral;
            float contrast = clip != null ? clip.Contrast : AdjustValue.Neutral;
            player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            player.SetAdjustFloat(VideoAdjustOption.Brightness, ClampAdjust(brightness));
            player.SetAdjustFloat(VideoAdjustOption.Contrast, ClampAdjust(contrast));
        }

        /// <summary>
        /// Cover-fit, not letterbox. Crops the source to the OUTPUT window's aspect ratio so a
        /// clip whose dimensions do not match the wall FILLS it -- losing left/right or top/bottom
        /// (whichever overflows) instead of showing black bars. The real wall is 1664x256 but the
        /// clips are authored 1964x256; without this they letterbox to ~217px tall. A clip already
        /// at the wall's ratio is cropped by nothing, so this is a no-op for correct content.
        ///
        /// CropGeometry as a "W:H" RATIO crops centred and does NOT distort. AspectRatio would
        /// stretch to fill and distort -- deliberately not used. Applied to both layers (the back
        /// clip must be cover-fitted before it swaps in) and re-applied on every ApplyGeometry so
        /// a geometry change re-crops to the new ratio.
        /// </summary>
        private void ApplyCropToFill(MediaPlayer player)
        {
            var ratio = CropRatio(_config.OutputWidth, _config.OutputHeight);
            if (ratio != null) player.CropGeometry = ratio;
        }

        /// <summary>
        /// The output aspect ratio as a reduced "W:H" crop string, or null for a not-yet-resolved
        /// (zero) geometry. Reduced by GCD so 1664x256 becomes "13:2" -- canonical and free of any
        /// large-number parsing quirk. Pure and static so the ratio maths is tested without libvlc.
        /// </summary>
        public static string CropRatio(int width, int height)
        {
            if (width <= 0 || height <= 0) return null;
            var g = Gcd(width, height);
            return $"{width / g}:{height / g}";
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) { var t = b; b = a % b; a = t; }
            return a;
        }

        /// <summary>
        /// The pending clip failed to play. Only the BACK player matters here: the front one
        /// erroring is a clip already on the wall, which Task 15 will have to look at, but there
        /// is nothing useful to do about it from here -- stopping it would blank the wall to no
        /// purpose.
        /// </summary>
        private void OnEncounteredError(bool isA)
        {
            if (isA == _frontIsA) return;
            _pendingFailed = true;
        }

        /// <summary>
        /// :input-repeat has counted down to zero -- roughly three weeks in. Restart, or the
        /// wall is black until someone notices. Runs on a libvlc thread: marshal, never
        /// re-enter libvlc from here.
        ///
        /// This restart is NOT seamless, and the two layers cannot help: the front player has
        /// already stopped, so there is no picture left to hold the wall while the incoming
        /// layer loads. Expect the ~290ms of black here, once every ~22 days. That is the
        /// trade being made, and it is a good one.
        /// </summary>
        private void OnEndReached(bool isA)
        {
            try
            {
                if (!_outputWindow.IsHandleCreated) return;
                _outputWindow.BeginInvoke((Action)(() => OnEndReachedOnUiThread(isA)));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnEndReachedOnUiThread(bool isA)
        {
            if (_disposed) return;

            // An outgoing layer finishing is none of our business -- only the clip actually
            // on the wall needs restarting.
            if (isA != _frontIsA) return;
            if (CurrentSlot == null) return;

            // Someone is already loading something else onto the back layer -- probably an
            // operator who pressed a button in the moment this fired. Their clip is about to
            // replace the wall anyway, and restarting from here would call CancelPending() and
            // silently throw their press away, leaving the button looking dead.
            if (_pendingSlot != null) return;

            var slot = CurrentSlot.Value;
            var path = _library.BySlot(slot)?.Path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _log($"Slot {slot} ended and its file is gone -- cannot restart.");
                return;
            }

            _log($"Slot {slot} reached the end of :input-repeat -- restarting.");
            StartLoad(slot, path);
        }

        private void RaiseUnavailable(int slot, string path, string reason)
        {
            _log($"Slot {slot} unavailable ({reason}): {path ?? "<none>"} -- wall unchanged.");
            ClipUnavailable?.Invoke(this, new ClipUnavailableEventArgs(slot, path, reason));
        }

        private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Teardown order matters and is the same one the spike arrived at the hard way: stop
        /// the players, detach the VideoViews from them (set_hwnd(NULL) against a STOPPED
        /// player), then dispose the window, then the players, then libvlc. Detaching a live
        /// player hangs.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            TearDown();
        }

        /// <summary>
        /// Best effort throughout, and null-tolerant throughout: this also runs from the
        /// constructor's catch, where any field past the point of failure is still null.
        /// </summary>
        private void TearDown()
        {
            _firstPictureTimer?.Stop();
            _firstPictureTimer?.Dispose();

            try { _playerA?.Stop(); } catch { /* best effort during teardown */ }
            try { _playerB?.Stop(); } catch { /* best effort during teardown */ }

            if (_outputWindow != null)
            {
                try
                {
                    _outputWindow.DetachPlayers();
                    _outputWindow.ShutDown();
                }
                catch { /* best effort during teardown */ }
                _outputWindow.Dispose();
            }

            _frontMedia?.Dispose();
            _pendingMedia?.Dispose();
            _playerA?.Dispose();
            _playerB?.Dispose();
            _libVlc?.Dispose();
        }
    }
}
