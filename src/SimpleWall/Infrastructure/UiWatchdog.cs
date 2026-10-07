using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SimpleWall.Infrastructure
{
    /// <summary>
    /// Restarts the app when its UI thread stops responding. See <see cref="WatchdogPolicy"/> for
    /// the incident that put this here.
    ///
    /// Three rules, and every one of them is about the watchdog being unable to die the same way
    /// the thing it watches died:
    ///
    ///   1. **It shares nothing with the UI thread but a long.** The watcher thread reads one
    ///      Interlocked timestamp and calls nothing else -- no Control.Invoke, no BeginInvoke, no
    ///      WinForms at all. A watchdog that pokes the deadlocked thread to ask whether it is
    ///      deadlocked is a watchdog that joins it. This is the whole design.
    ///   2. **The heartbeat is the thing that actually failed.** <see cref="Beat"/> is called from
    ///      MainForm's existing one-second scheduler timer, which runs on the message pump. That
    ///      pump is precisely what stopped on 07-30 -- 15 cues missed proves it -- so watching it
    ///      is watching the real symptom rather than a proxy for it. No second timer: one
    ///      mechanism, already proven to stop when it matters.
    ///   3. **It kills with TerminateProcess, not Environment.Exit.** Exit runs finalizers and an
    ///      AppDomain unload, either of which can end up waiting on the very thread that is stuck.
    ///      Hanging while killing a hang would be the whole exercise wasted. The OS reclaims the
    ///      window and the libvlc natives regardless -- Program's crash handler already makes this
    ///      exact argument and for the same reason.
    ///
    /// Not a general-purpose watchdog: it fires once and then the process is gone.
    /// </summary>
    public sealed class UiWatchdog : IDisposable
    {
        /// <summary>
        /// How long the relauncher waits before starting the new instance.
        ///
        /// Not padding -- it is the single-instance mutex. Program holds Local\SimpleWall-{guid}
        /// for the life of the process, so a replacement that starts before this one is gone gets
        /// the "SimpleWall is already running" dialog and exits, leaving nothing on the wall at
        /// all: strictly worse than the hang. The kill below is a TerminateProcess and takes
        /// effect immediately, so five seconds is already several orders of margin.
        /// </summary>
        private const int RelaunchDelaySeconds = 5;

        private const int MaxCheckIntervalMs = 5000;
        private const int MinCheckIntervalMs = 25;

        private readonly TimeSpan _threshold;
        private readonly int _checkIntervalMs;
        private readonly Action<string> _log;
        private readonly Action _onHung;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

        /// <summary>Stopwatch ticks. Written by the UI thread, read by the watcher thread.</summary>
        private long _lastBeat;

        private Thread _watcher;
        private int _fired;
        private bool _disposed;

        /// <param name="threshold">
        /// Silence beyond this is a hang. Zero or less switches the watchdog off entirely.
        /// </param>
        /// <param name="onHung">
        /// What to do about it. Injectable ONLY so the tests can prove the detection without
        /// killing the test runner; production always gets <see cref="RestartProcess"/>.
        /// </param>
        public UiWatchdog(TimeSpan threshold, Action<string> log = null, Action onHung = null)
        {
            _threshold = threshold;
            _log = log ?? (_ => { });
            _onHung = onHung ?? RestartProcess;

            // Checked four times per threshold, so detection lands within ~25% of it, capped at
            // five seconds so a sixty-second threshold does not mean a sixty-second blind spot
            // and floored so the tests can run a sub-second threshold without a sleep-heavy suite.
            _checkIntervalMs = (int)Math.Max(MinCheckIntervalMs,
                Math.Min(MaxCheckIntervalMs, threshold.TotalMilliseconds / 4));

            _lastBeat = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// The heartbeat. Called from the UI thread; costs one interlocked write, which is why it
        /// can sit in a one-second timer that already runs unconditionally.
        /// </summary>
        public void Beat() => Interlocked.Exchange(ref _lastBeat, Stopwatch.GetTimestamp());

        /// <summary>
        /// Arms the watchdog. Call once the message pump is about to run -- earlier and the
        /// heartbeat cannot possibly arrive, which is indistinguishable from a hang.
        /// </summary>
        public void Start()
        {
            if (_threshold <= TimeSpan.Zero)
            {
                _log("Watchdog disabled (WatchdogSeconds = 0): a hung UI thread will NOT be restarted.");
                return;
            }

            if (_watcher != null || _disposed) return;

            // Re-stamped here rather than trusting the constructor's: everything between the two
            // is startup work on the UI thread (libvlc, the output window, the default clip) and
            // none of it beats.
            Beat();

            _watcher = new Thread(Watch)
            {
                IsBackground = true,
                Name = "SimpleWall UI watchdog"
            };
            _watcher.Start();

            _log($"Watchdog armed: the app restarts itself if the UI thread stops responding for " +
                 $"{_threshold.TotalSeconds:F0}s.");
        }

        private void Watch()
        {
            // Wait returns true when Dispose signals, which is the only way out other than firing.
            while (!_stop.Wait(_checkIntervalMs))
            {
                var since = Elapsed(Interlocked.Read(ref _lastBeat));
                if (!WatchdogPolicy.IsHung(since, _threshold)) continue;

                // Once. The process is about to end; a second pass through here could only ever
                // race the first one's Process.Start and put two instances on the wall.
                if (Interlocked.Exchange(ref _fired, 1) != 0) return;

                Fire(since);
                return;
            }
        }

        private void Fire(TimeSpan since)
        {
            // Logged BEFORE acting, and it survives: Log.Append opens, writes and closes the file
            // per line, so this reaches disk before the kill. It is the ONLY thing that will ever
            // say this happened -- the thread that would otherwise report it is the stuck one --
            // and without it the next restart looks exactly like somebody power-cycling the PC.
            _log($"WATCHDOG: the UI thread has not responded for {since.TotalSeconds:F0}s. The wall is " +
                 "still playing but nothing can change it (no scheduler, no OSC, no mouse). Restarting.");

            try
            {
                _onHung();
            }
            catch (Exception ex)
            {
                _log("WATCHDOG: the restart failed, so the app is being left hung rather than killed: " + ex);
            }
        }

        /// <summary>
        /// Schedule a replacement, then terminate.
        ///
        /// In that order, and the order is the point: if the relauncher cannot be started we must
        /// NOT kill anyway. A hung app is still showing a looping clip on the wall; a killed app
        /// with nothing to replace it shows the Windows desktop, and autostart is an HKCU\Run
        /// value that only fires at logon, so nothing would bring it back until somebody walks in.
        /// Failing to restart is bad. Failing to restart AND taking the picture down is worse.
        /// </summary>
        private void RestartProcess()
        {
            var exe = Application.ExecutablePath;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",

                    // ping rather than timeout.exe: timeout fails outright ("ERROR: Input
                    // redirection is not supported") when it has no console of its own, which is
                    // exactly the situation here. ping -n N sleeps N-1 seconds against the
                    // loopback and needs nothing from the network. The empty "" is start's title
                    // argument -- without it, start treats the quoted path AS the title and opens
                    // a console window instead of the app.
                    Arguments = $"/c ping -n {RelaunchDelaySeconds + 1} 127.0.0.1 > nul & start \"\" \"{exe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
                });
            }
            catch (Exception ex)
            {
                _log("WATCHDOG: could not schedule the relaunch, so the app is being left hung " +
                     "rather than killed with nothing to replace it: " + ex.Message);
                return;
            }

            // See rule 3 in the class docs: TerminateProcess, because Environment.Exit can block
            // on the thread we are killing the process to escape.
            Process.GetCurrentProcess().Kill();
        }

        private static TimeSpan Elapsed(long stamp) =>
            TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - stamp) / Stopwatch.Frequency);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // First, before anything can be signalled: a check already in flight must not decide
            // to kill the process on its way out of a NORMAL shutdown. Program disposes this
            // innermost, so it happens the moment Application.Run returns -- while the message
            // pump has legitimately stopped and the beat has legitimately stopped with it.
            Interlocked.Exchange(ref _fired, 1);

            _stop.Set();

            var watcher = _watcher;
            _watcher = null;

            // Background thread, so it can never hold the process open; the join is only so a
            // check that is mid-flight is finished with before teardown continues.
            try { watcher?.Join(TimeSpan.FromSeconds(2)); }
            catch (ThreadStateException) { /* never started; nothing to wait for */ }

            _stop.Dispose();
        }
    }
}
