using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Rug.Osc;
using SimpleWall.Engine;
using SimpleWall.Osc;
using Xunit;

// Rug.Osc has an OscListener of its own (an address-pattern dispatcher, not this). Ours wins
// inside SimpleWall.Osc by namespace precedence, but a test importing both needs to say which.
using OscListener = SimpleWall.Osc.OscListener;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Real sockets, real datagrams, real Rug.Osc decoding. The parser is already covered by pure
    /// tests; what these prove is the part that can only fail in production: that the library
    /// actually works on net48 (the package ships one un-TFM'd assembly, so compiling against it
    /// proves nothing about loading it), and that the receive loop survives what a UDP port
    /// actually receives.
    /// </summary>
    public class OscListenerTests : IDisposable
    {
        private readonly List<OscListener> _listeners = new List<OscListener>();
        private readonly UdpClient _sender = new UdpClient();

        public void Dispose()
        {
            foreach (var listener in _listeners) listener.Dispose();
            _sender.Close();
        }

        /// <summary>Port 0 lets the OS pick, so tests never collide with each other or the app.</summary>
        private OscListener StartListener(out BlockingCollector collector)
        {
            var listener = new OscListener(0);
            _listeners.Add(listener);
            collector = new BlockingCollector(listener);
            Assert.True(listener.Start(), "the listener could not bind an ephemeral port");
            return listener;
        }

        private void Send(int port, OscMessage message)
        {
            var bytes = message.ToByteArray();
            _sender.Send(bytes, bytes.Length, "127.0.0.1", port);
        }

        private void SendRaw(int port, byte[] bytes) => _sender.Send(bytes, bytes.Length, "127.0.0.1", port);

        [Fact]
        public void ARealPacketOffTheWireBecomesACommand()
        {
            var listener = StartListener(out var collector);

            Send(listener.BoundPort, new OscMessage("/clip/7"));

            var command = collector.Next();
            Assert.Equal(CommandKind.PlayClip, command.Kind);
            Assert.Equal(7, command.Slot);
        }

        [Fact]
        public void BrightnessArrivesWithItsValue()
        {
            var listener = StartListener(out var collector);

            Send(listener.BoundPort, new OscMessage("/brightness", 0.5f));

            var command = collector.Next();
            Assert.Equal(CommandKind.Brightness, command.Kind);
            Assert.Equal(0.5f, command.Value);
        }

        /// <summary>
        /// The Stream Deck's release. It must not re-trigger -- end to end, not just in the parser.
        /// </summary>
        [Fact]
        public void AButtonReleaseArrivesAndIsIgnored()
        {
            var listener = StartListener(out var collector);

            Send(listener.BoundPort, new OscMessage("/clip/7", 0f));  // release: ignored
            Send(listener.BoundPort, new OscMessage("/stop"));        // ...but this still lands

            // The stop proves the release was seen and dropped, rather than merely being slow.
            Assert.Equal(CommandKind.Stop, collector.Next().Kind);
        }

        /// <summary>
        /// THE rule. Anything can arrive on a UDP port -- a scanner, a truncated packet, another
        /// protocol entirely. One unhandled exception would silently end remote control for the
        /// evening and nobody would find out until a button didn't work mid-show.
        /// </summary>
        [Fact]
        public void GarbageDoesNotKillTheListener()
        {
            var listener = StartListener(out var collector);

            SendRaw(listener.BoundPort, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            SendRaw(listener.BoundPort, new byte[0]);
            SendRaw(listener.BoundPort, System.Text.Encoding.ASCII.GetBytes("this is not OSC at all"));
            SendRaw(listener.BoundPort, new byte[] { (byte)'/', (byte)'c', (byte)'l' }); // truncated address

            Send(listener.BoundPort, new OscMessage("/play"));

            Assert.Equal(CommandKind.Play, collector.Next().Kind);
        }

        /// <summary>Some senders wrap even a single message in a bundle.</summary>
        [Fact]
        public void ABundleIsUnwrapped()
        {
            var listener = StartListener(out var collector);

            Send(listener.BoundPort, new OscMessage("/nonsense")); // ignored, keeps ordering honest
            var bundle = new OscBundle(new OscTimeTag(0), new OscMessage("/clip/3"));
            var bytes = bundle.ToByteArray();
            SendRaw(listener.BoundPort, bytes);

            var command = collector.Next();
            Assert.Equal(CommandKind.PlayClip, command.Kind);
            Assert.Equal(3, command.Slot);
        }

        /// <summary>
        /// A taken port is not fatal: the wall runs fine without remote control, and refusing to
        /// start over it would be absurd. Autostart racing a manual launch makes this real.
        /// </summary>
        [Fact]
        public void APortAlreadyInUseFailsSoftly()
        {
            using (var hog = new UdpClient())
            {
                hog.ExclusiveAddressUse = true;
                hog.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0));
                var taken = ((System.Net.IPEndPoint)hog.Client.LocalEndPoint).Port;

                var listener = new OscListener(taken);
                _listeners.Add(listener);
                string reported = null;
                listener.Failed += (s, message) => reported = message;

                var started = listener.Start(); // must not throw

                Assert.False(started);
                Assert.NotNull(reported);
                Assert.Contains(taken.ToString(), reported);
            }
        }

        /// <summary>
        /// The riskiest branch in the class, and it had no coverage: every other test passes
        /// sync: null, which takes the synchronous path by design.
        ///
        /// ISynchronizeInvoke.InvokeRequired returns FALSE when the handle does not exist -- not
        /// only when you are already on the UI thread. Since Raise only ever runs on the receive
        /// thread, an `if (InvokeRequired) BeginInvoke else call()` would call the handler HERE,
        /// off-thread, straight into an engine that drives native libvlc. The window with no handle
        /// is real twice per run: before Application.Run creates it, and after the form closes
        /// while this thread is still receiving.
        /// </summary>
        [Fact]
        public void APacketArrivingBeforeTheWindowHasAHandleIsDroppedNotRunOnTheReceiveThread()
        {
            using (var form = new System.Windows.Forms.Form())
            {
                Assert.False(form.IsHandleCreated, "the test needs a form whose handle does not exist yet");

                var listener = new OscListener(0, form);
                _listeners.Add(listener);

                var ran = 0;
                listener.CommandReceived += (s, c) => Interlocked.Increment(ref ran);
                Assert.True(listener.Start());

                Send(listener.BoundPort, new OscMessage("/clip/1"));
                Thread.Sleep(750); // generous: the packet is loopback and arrives in microseconds

                Assert.Equal(0, ran);
            }
        }

        /// <summary>Collects commands off the listener's thread and waits for them.</summary>
        private class BlockingCollector
        {
            private readonly Queue<WallCommand> _commands = new Queue<WallCommand>();
            private readonly SemaphoreSlim _arrived = new SemaphoreSlim(0);

            public BlockingCollector(OscListener listener)
            {
                listener.CommandReceived += (s, command) =>
                {
                    lock (_commands) _commands.Enqueue(command);
                    _arrived.Release();
                };
            }

            public WallCommand Next()
            {
                Assert.True(_arrived.Wait(TimeSpan.FromSeconds(5)), "no OSC command arrived within 5s");
                lock (_commands) return _commands.Dequeue();
            }
        }
    }
}
