using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Rug.Osc;
using SimpleWall.Engine;

namespace SimpleWall.Osc
{
    /// <summary>
    /// The UDP socket around <see cref="OscParser"/>. Everything about what a message MEANS lives
    /// in the parser, which is pure and tested; this only turns datagrams into calls.
    ///
    /// Two rules, and they are the whole job:
    ///
    ///   1. **The receive loop never dies.** Anything can arrive on a UDP port -- a port scanner,
    ///      a truncated packet, another protocol entirely, a Stream Deck plugin's idea of OSC. A
    ///      single unhandled exception here would silently end remote control for the evening,
    ///      and nobody would know until a button didn't work mid-show.
    ///   2. **A port already in use is not fatal.** The app runs fine without OSC; it just can't
    ///      be triggered remotely. Refusing to start the wall over it would be absurd.
    ///
    /// Threading: the loop runs on its own thread, and the engine is UI-thread-only, so every
    /// command is marshalled before it is raised. This is the first thing in the app that calls
    /// the engine from off the UI thread.
    /// </summary>
    public class OscListener : IDisposable
    {
        private readonly int _port;
        private readonly Control _sync;
        private readonly Action<string> _log;

        private UdpClient _client;
        private Thread _thread;
        private volatile bool _stopping;
        private bool _disposed;

        /// <param name="sync">
        /// What to marshal onto -- the main form in production. A Control, not ISynchronizeInvoke,
        /// because deciding safely needs IsHandleCreated and that interface cannot answer it. Null
        /// raises on the receive thread and is only appropriate for a test.
        /// </param>
        public OscListener(int port, Control sync = null, Action<string> log = null)
        {
            _port = port;
            _sync = sync;
            _log = log ?? (_ => { });
        }

        /// <summary>Raised on the UI thread when <paramref name="sync"/> was supplied.</summary>
        public event EventHandler<WallCommand> CommandReceived;

        /// <summary>The port was unusable. The app carries on without remote control.</summary>
        public event EventHandler<string> Failed;

        /// <summary>The port actually bound, useful when 0 was asked for. -1 before Start.</summary>
        public int BoundPort => (_client?.Client?.LocalEndPoint as IPEndPoint)?.Port ?? -1;

        /// <summary>False if the port could not be opened. Not an exception -- see the class docs.</summary>
        public bool Start()
        {
            if (_disposed) return false;

            try
            {
                _client = new UdpClient(_port);
            }
            catch (SocketException ex)
            {
                // Almost always a second instance of this app, which is a live known issue
                // (autostart racing a manual launch). Say so rather than dying.
                var message = $"OSC port {_port} could not be opened ({ex.SocketErrorCode}). " +
                              "Remote control is off for this session; everything else works.";
                _log(message);
                Failed?.Invoke(this, message);
                return false;
            }

            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OSC listener" };
            _thread.Start();
            _log($"OSC listening on port {BoundPort}");
            return true;
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);

            // Held locally: Dispose nulls the field, and reading it here would then NullReference
            // on the way out and log "OSC packet ignored: Object reference not set" on every
            // clean shutdown -- sending whoever reads that log hunting a packet that never existed.
            var client = _client;

            while (!_stopping)
            {
                try
                {
                    var data = client.Receive(ref remote);
                    Handle(data);
                }
                catch (ObjectDisposedException)
                {
                    return; // Dispose closed the socket underneath us. Expected.
                }
                catch (SocketException) when (_stopping)
                {
                    return; // Same, on the platforms that report it this way.
                }
                catch (SocketException ex)
                {
                    // Defensive: no specific cause is known for this socket (it only ever
                    // receives, so it cannot elicit the ICMP port-unreachable that bites sockets
                    // which also send). Whatever it is, it is not a reason to stop listening.
                    //
                    // The sleep is the point: if Receive ever fails persistently and instantly --
                    // the NIC going away, say -- this would otherwise spin a core flat out and
                    // write a log line per iteration, forever, on a machine nobody is watching.
                    _log($"OSC socket error, still listening: {ex.SocketErrorCode}");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    // A malformed packet must not take remote control down for the evening.
                    _log($"OSC packet ignored: {ex.Message}");
                }
            }
        }

        private void Handle(byte[] data)
        {
            var packet = OscPacket.Read(data, data.Length);

            foreach (var message in Flatten(packet))
            {
                var command = OscParser.Parse(message.Address, message.ToArray());
                if (command == null) continue; // unknown, malformed, or a button release
                Raise(command);
            }
        }

        /// <summary>
        /// Bundles are unwrapped, recursively. Some OSC senders wrap even a single message in a
        /// bundle, and refusing them would look exactly like "OSC doesn't work" -- with no clue
        /// as to whether the app, the plugin, the network or the firewall was to blame.
        /// </summary>
        private static IEnumerable<OscMessage> Flatten(OscPacket packet)
        {
            switch (packet)
            {
                case OscMessage message:
                    yield return message;
                    break;

                case OscBundle bundle:
                    foreach (var inner in bundle)
                        foreach (var message in Flatten(inner))
                            yield return message;
                    break;
            }
        }

        /// <summary>
        /// Marshals, or drops. It never runs the handler on this thread when a UI target exists.
        ///
        /// The obvious `if (InvokeRequired) BeginInvoke else call()` is a trap here:
        /// InvokeRequired returns FALSE when the target's handle does not exist -- not only when
        /// you are already on the right thread. The handle is absent twice in every run: before
        /// Application.Run creates it, and again once the form closes while this thread is still
        /// receiving. Falling through would call Execute on the RECEIVE thread, and the engine is
        /// UI-thread-only by contract: it drives VideoViews and native libvlc. Off-thread libvlc
        /// in this project has already been measured as a 0xC0000005 that the crash handler never
        /// sees.
        ///
        /// So an early or late packet is dropped and logged. Losing a button press in the second
        /// before the window exists is a nuisance; corrupting the engine is the wall.
        /// </summary>
        private void Raise(WallCommand command)
        {
            var handler = CommandReceived;
            if (handler == null) return;

            if (_sync == null)
            {
                // No UI to marshal onto: tests only. Production always passes the form.
                handler(this, command);
                return;
            }

            if (!_sync.IsHandleCreated || _sync.IsDisposed)
            {
                _log($"OSC {command.Kind} dropped: the window is not ready (starting up or closing).");
                return;
            }

            try
            {
                _sync.BeginInvoke((Action)(() => handler(this, command)));
            }
            catch (ObjectDisposedException)
            {
                _log($"OSC {command.Kind} dropped: the window closed mid-flight.");
            }
            catch (InvalidOperationException)
            {
                _log($"OSC {command.Kind} dropped: the window has no handle.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopping = true;

            // Closing the socket is what wakes the blocking Receive; the thread is a background
            // thread, so a stuck one can never hold the process open either way.
            _client?.Close();
            _client = null;
        }
    }
}
