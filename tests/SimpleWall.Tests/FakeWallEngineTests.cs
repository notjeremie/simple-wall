using SimpleWall.Engine;
using Xunit;

namespace SimpleWall.Tests
{
    public class FakeWallEngineTests
    {
        [Fact]
        public void ExecuteRecordsTheCommand()
        {
            var engine = new FakeWallEngine();
            var command = WallCommand.Simple(CommandKind.Stop);

            engine.Execute(command);

            Assert.Single(engine.Received);
            Assert.Same(command, engine.Received[0]);
        }

        [Fact]
        public void ExecutePlayClipUpdatesCurrentSlotAndIsPlaying()
        {
            var engine = new FakeWallEngine();

            engine.Execute(WallCommand.PlayClip(3));

            Assert.Equal(3, engine.CurrentSlot);
            Assert.True(engine.IsPlaying);
        }

        [Fact]
        public void ExecuteRaisesStateChanged()
        {
            var engine = new FakeWallEngine();
            var raised = false;
            engine.StateChanged += (sender, args) => raised = true;

            engine.Execute(WallCommand.Simple(CommandKind.Stop));

            Assert.True(raised);
        }
    }
}
