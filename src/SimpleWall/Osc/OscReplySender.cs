using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Rug.Osc;
using SimpleWall.Engine;
using SimpleWall.Model;

namespace SimpleWall.Osc
{
    /// <summary>
    /// Pushes what the wall is doing back out, so a Stream Deck can light its own buttons to match
    /// rather than guessing. Same honesty rule as the grid: this reports the ENGINE's state, not
    /// what was last asked for.
    ///
    /// No reply host configured means silence -- not an error, and not a socket. That is the
    /// default, and the common case.
    /// </summary>
    public class OscReplySender : IDisposable
    {
        public const string SlotAddress = "/state/slot";
        public const string PlayingAddress = "/state/playing";
        public const string BrightnessAddress = "/state/brightness";
        public const string ContrastAddress = "/state/contrast";

        /// <summary>Sent for "nothing on the wall" -- OSC has no null, and 0 is not a valid slot.</summary>
        public const int NoSlot = 0;

        private readonly IWallEngine _engine;
        private readonly WallConfig _config;
        private readonly Action<string> _log;

        private UdpClient _client;

        /// <summary>
        /// Resolved ONCE, never on the send path. Null until it is known, which is why every send
        /// checks it. Volatile because it is written by the resolver thread and read by the UI one.
        /// </summary>
        private volatile IPEndPoint _target;

        private bool _disposed;

        public OscReplySender(IWallEngine engine, WallConfig config, Action<string> log = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? (_ => { });
        }

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_config.OscReplyHost);

        public void Start()
        {
            if (!IsEnabled) return;

            try
            {
                _client = new UdpClient();
                _engine.StateChanged += OnStateChanged;
                ResolveTarget();
            }
            catch (Exception ex)
            {
                _log($"OSC replies disabled -- could not open a socket: {ex.Message}");
            }
        }

        /// <summary>
        /// Works out the address ONCE, and never on the send path.
        ///
        /// This is not a micro-optimisation. UdpClient.Send(bytes, length, hostname, port) resolves
        /// the name on EVERY call, and Send runs on the UI thread (StateChanged does). A bare
        /// hostname -- "streamdeck-pc", i.e. exactly what someone types into OscReplyHost -- sends
        /// Windows through DNS suffix search, then LLMNR, then a NetBIOS broadcast: measured at
        /// ~10 SECONDS per call, and failures are not negatively cached, so it is 10s EVERY time.
        /// With a fader sweep at ~100 packets a second each queueing another 10s block, the UI
        /// thread never catches up and the wall wedges permanently. Not a stutter -- a wedge.
        ///
        /// A literal IP resolves synchronously and costs nothing, which is the common case and
        /// keeps replies working from the first frame. A name is resolved on a background thread,
        /// because doing it here would just move the same 10s freeze into startup.
        /// </summary>
        private void ResolveTarget()
        {
            var host = _config.OscReplyHost.Trim();
            var port = _config.OscReplyPort;

            if (IPAddress.TryParse(host, out var literal))
            {
                _target = new IPEndPoint(literal, port);
                _log($"OSC replies to {_target}");
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var addresses = Dns.GetHostAddresses(host);
                    var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                                  ?? addresses.FirstOrDefault();

                    if (address == null)
                    {
                        _log($"OSC replies disabled -- '{host}' resolved to nothing.");
                        return;
                    }

                    if (_disposed) return;
                    _target = new IPEndPoint(address, port);
                    _log($"OSC replies to {_target} (resolved from '{host}')");
                }
                catch (Exception ex)
                {
                    // The reply target being unreachable is not this app's problem to solve, and
                    // certainly not one to retry on the send path.
                    _log($"OSC replies disabled -- could not resolve '{host}': {ex.Message}");
                }
            });
        }

        private void OnStateChanged(object sender, EventArgs e) => Send();

        /// <summary>
        /// Best effort by design. The reply target is somebody else's machine, which may be off,
        /// renamed, or on a network that no longer exists -- and none of that is a reason for the
        /// wall to so much as flicker.
        /// </summary>
        public void Send()
        {
            // No socket, or the address isn't known yet (a hostname still resolving, or one that
            // never will). Either way there is nowhere to send and nothing to wait for.
            if (_client == null || _target == null) return;

            try
            {
                SendMessage(new OscMessage(SlotAddress, _engine.CurrentSlot ?? NoSlot));
                SendMessage(new OscMessage(PlayingAddress, _engine.IsPlaying ? 1 : 0));
                // The CLIP's look on the wall now, from the engine -- NOT _config.Brightness/Contrast,
                // which the clip-looks migration froze at neutral. Clamped again at this boundary:
                // the production engine clamps, but nothing out on the network should ever receive a
                // NaN/overflow, so the reply does not depend on that promise being kept.
                SendMessage(new OscMessage(BrightnessAddress, AdjustValue.Clamp(_engine.CurrentBrightness)));
                SendMessage(new OscMessage(ContrastAddress, AdjustValue.Clamp(_engine.CurrentContrast)));
            }
            catch (Exception ex)
            {
                _log($"OSC reply failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends to a resolved IPEndPoint. Never the (hostname, port) overload -- that is the one
        /// that resolves on every call; see ResolveTarget.
        /// </summary>
        private void SendMessage(OscMessage message)
        {
            var bytes = message.ToByteArray();
            _client.Send(bytes, bytes.Length, _target);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _engine.StateChanged -= OnStateChanged;
            _client?.Close();
            _client = null;
        }
    }
}
