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
        /// How long to wait for the incoming layer to produce a picture before giving up on
        /// it. Measured first-picture on the real machine was ~286ms; this is deliberately
        /// far looser, because the cost of being wrong is asymmetric -- abandoning a clip
        /// that was merely slow blanks nothing (the outgoing clip keeps the wall), but the
        /// operator's button appears dead.
        /// </summary>
        private static readonly TimeSpan FirstPictureTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A WinForms timer floors at ~15.6ms regardless of what is asked for, so this is
        /// "as fast as the UI thread will do", not a precise 15ms. Polling VoutCount is how
        /// the spike measured first-picture; the Playing event fires ~170ms too early
        /// (112ms vs 286ms), and swapping on it would show exactly the black frame the two
        /// layers exist to hide.
        /// </summary>
        private const int PollIntervalMs = 15;

        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _playerA;
        private readonly MediaPlayer _playerB;
        private readonly OutputWindow _outputWindow;
        private readonly ClipLibrary _library;
        private readonly WallConfig _config;
        private readonly Timer _firstPictureTimer;
        private readonly Stopwatch _loadStopwatch = new Stopwatch();
        private readonly Action<string> _log;

        private bool _frontIsA = true;
        private int? _pendingSlot;
        private Media _frontMedia;
        private Media _pendingMedia;
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

                _outputWindow = new OutputWindow(_playerA, _playerB);
                ApplyGeometry();
                ApplyAdjust(_playerA);
                ApplyAdjust(_playerB);

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

            // Shown BEFORE Play, never after: libvlc builds its vout against whatever window
            // it is handed at that moment. A window that is invisible at Play and shown
            // afterwards is untested territory on this GPU, and the failure mode is a black
            // wall, not an exception.
            if (!_outputWindow.Visible) _outputWindow.Show();

            var back = BackPlayer;
            back.Play(media);

            // After Play, matching the spike: the adjust filter is inserted into a live vout.
            ApplyAdjust(back);

            _loadStopwatch.Restart();
            _firstPictureTimer.Start();
            _log($"Loading slot {slot} on back layer: {path}");
        }

        /// <summary>
        /// The swap. Polls until the incoming layer reports a video output, which is the
        /// closest available proxy for "there is a frame on the wall", then flips z-order.
        /// </summary>
        private void OnFirstPictureTick(object sender, EventArgs e)
        {
            if (_pendingSlot == null)
            {
                _firstPictureTimer.Stop();
                return;
            }

            if (BackPlayer.VoutCount > 0)
            {
                CompleteSwap();
                return;
            }

            if (_loadStopwatch.Elapsed > FirstPictureTimeout)
            {
                var slot = _pendingSlot.Value;
                var path = _library.BySlot(slot)?.Path;
                _log($"Slot {slot} produced no picture after {FirstPictureTimeout.TotalSeconds}s -- abandoning, wall unchanged.");
                CancelPending();
                RaiseUnavailable(slot, path, "the clip produced no picture");
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

        private void SetBrightness(float value)
        {
            _config.Brightness = ClampAdjust(value);
            ApplyAdjust(_playerA);
            ApplyAdjust(_playerB);
            RaiseStateChanged();
        }

        private void SetContrast(float value)
        {
            _config.Contrast = ClampAdjust(value);
            ApplyAdjust(_playerA);
            ApplyAdjust(_playerB);
            RaiseStateChanged();
        }

        /// <summary>
        /// The engine clamps rather than trusting its callers, because it cannot see where a
        /// value came from and one route is already unguarded: config.json is deliberately NOT
        /// range-validated on load (a documented decision), so a hand-edited or half-corrupted
        /// "Brightness": 50 arrives here at startup and goes straight to a native SetAdjustFloat.
        /// OscParser clamps its own inputs, but IWallEngine is the single point of contact for
        /// the mouse and the scheduler too, so the clamp belongs on this side of the boundary.
        ///
        /// NaN maps to neutral, not to 0 or 2: Math.Min/Max PROPAGATE NaN rather than clamping
        /// it (the same trap that already got past one review on the OSC side), and a wall stuck
        /// at an undefined brightness is worse than one at 1.0.
        /// </summary>
        private static float ClampAdjust(float value) =>
            float.IsNaN(value) ? 1f : Math.Max(0f, Math.Min(2f, value));

        /// <summary>
        /// Applied to BOTH players, always: the back layer must already be at the right
        /// brightness before it is swapped in, or every clip change would flash at the old
        /// value for a frame.
        ///
        /// Note this writes the in-memory config but does NOT save it. Saving here would mean
        /// one atomic file write per OSC packet, and an OSC fader sweep is ~100 packets a
        /// second. Persistence is the app's job (Task 14), on a debounce or at exit.
        /// </summary>
        private void ApplyAdjust(MediaPlayer player)
        {
            // Clamped on read as well as on write: this also runs from the constructor, where
            // the values came from config.json and nothing has vetted them.
            player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            player.SetAdjustFloat(VideoAdjustOption.Brightness, ClampAdjust(_config.Brightness));
            player.SetAdjustFloat(VideoAdjustOption.Contrast, ClampAdjust(_config.Contrast));
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
