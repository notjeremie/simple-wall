using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rug.Osc;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Osc;
using Xunit;

namespace SimpleWall.Tests
{
    public class OscReplySenderTests : IDisposable
    {
        private readonly UdpClient _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        private int Port => ((IPEndPoint)_receiver.Client.LocalEndPoint).Port;

        public OscReplySenderTests() => _receiver.Client.ReceiveTimeout = 5000;

        public void Dispose() => _receiver.Close();

        private Dictionary<string, object> ReceiveAll(int count)
        {
            var received = new Dictionary<string, object>();
            var remote = new IPEndPoint(IPAddress.Any, 0);

            for (var i = 0; i < count; i++)
            {
                var datagram = _receiver.Receive(ref remote);
                var message = (OscMessage)OscPacket.Read(datagram, datagram.Length);
                received[message.Address] = message[0];
            }

            return received;
        }

        [Fact]
        public void StateChangedPushesTheWholeStateOut()
        {
            var config = new WallConfig
            {
                OscReplyHost = "127.0.0.1",
                OscReplyPort = Port,
                Brightness = 0.5f,
                Contrast = 1.5f
            };
            var engine = new FakeWallEngine { CurrentSlot = 4, IsPlaying = true };

            using (var sender = new OscReplySender(engine, config))
            {
                sender.Start();
                engine.Execute(WallCommand.PlayClip(4)); // raises StateChanged

                var received = ReceiveAll(4);

                Assert.Equal(4, received[OscReplySender.SlotAddress]);
                Assert.Equal(1, received[OscReplySender.PlayingAddress]);
                Assert.Equal(0.5f, received[OscReplySender.BrightnessAddress]);
                Assert.Equal(1.5f, received[OscReplySender.ContrastAddress]);
            }
        }

        /// <summary>
        /// OSC has no null, so "nothing on the wall" needs a value a Stream Deck can act on. 0 is
        /// not a valid slot, which makes it the honest choice.
        /// </summary>
        [Fact]
        public void NothingOnTheWallIsSentAsSlotZero()
        {
            var config = new WallConfig { OscReplyHost = "127.0.0.1", OscReplyPort = Port };
            var engine = new FakeWallEngine { CurrentSlot = null, IsPlaying = false };

            using (var sender = new OscReplySender(engine, config))
            {
                sender.Start();
                sender.Send();

                var received = ReceiveAll(4);

                Assert.Equal(OscReplySender.NoSlot, received[OscReplySender.SlotAddress]);
                Assert.Equal(0, received[OscReplySender.PlayingAddress]);
            }
        }

        [Fact]
        public void AnUnsetReplyHostIsSilentNotAnError()
        {
            var config = new WallConfig { OscReplyHost = "" };
            var engine = new FakeWallEngine();

            using (var sender = new OscReplySender(engine, config))
            {
                Assert.False(sender.IsEnabled);

                sender.Start();          // must not throw or open a socket
                sender.Send();           // must not throw
                engine.Execute(WallCommand.Simple(CommandKind.Stop)); // must not throw
            }
        }

        /// <summary>
        /// The one that matters, and it is about DURATION, not exceptions.
        ///
        /// UdpClient.Send(bytes, len, HOSTNAME, port) resolves on every call, and Send runs on the
        /// UI thread. A bare hostname -- exactly what someone types into OscReplyHost -- measured
        /// ~10 SECONDS per call on Windows (DNS suffix search, then LLMNR, then a NetBIOS
        /// broadcast), uncached, every time. At ~100 fader packets a second the UI thread never
        /// catches up and the wall wedges for good.
        ///
        /// A bare name with no dot, deliberately: an earlier version of this test used
        /// "no-such-host.invalid", which is the ONE unreachable name that is fast (RFC 2606
        /// guarantees NXDOMAIN and it is negatively cached), and asserted only "does not throw" --
        /// which was never the risk. It passed while the bug was wide open.
        /// </summary>
        [Fact]
        public void AnUnresolvableReplyHostNeverBlocksTheCaller()
        {
            var config = new WallConfig
            {
                OscReplyHost = "no-such-machine-on-this-network",
                OscReplyPort = 9000
            };
            var engine = new FakeWallEngine { CurrentSlot = 1, IsPlaying = true };

            using (var sender = new OscReplySender(engine, config))
            {
                var stopwatch = Stopwatch.StartNew();

                sender.Start();
                sender.Send();
                engine.Execute(WallCommand.PlayClip(1)); // raises StateChanged -> Send
                sender.Send();

                stopwatch.Stop();

                // Resolution happens on a background thread; nothing here may wait for it.
                Assert.True(stopwatch.ElapsedMilliseconds < 2000,
                    $"replies blocked the caller for {stopwatch.ElapsedMilliseconds}ms -- on the UI " +
                    "thread this is the wall freezing, not a slow reply.");
            }
        }

        /// <summary>Out-of-range config values must not leave here unclamped either.</summary>
        [Fact]
        public void ARidiculousConfigBrightnessIsClampedOnTheWayOut()
        {
            var config = new WallConfig
            {
                OscReplyHost = "127.0.0.1",
                OscReplyPort = Port,
                Brightness = float.NaN,
                Contrast = 50f
            };
            var engine = new FakeWallEngine();

            using (var sender = new OscReplySender(engine, config))
            {
                sender.Start();
                sender.Send();

                var received = ReceiveAll(4);

                Assert.Equal(AdjustValue.Neutral, received[OscReplySender.BrightnessAddress]);
                Assert.Equal(AdjustValue.Max, received[OscReplySender.ContrastAddress]);
            }
        }
    }
}
