using SimpleWall.Engine;
using SimpleWall.Model;
using Xunit;

namespace SimpleWall.Tests
{
    public class WallCommandTests
    {
        [Fact]
        public void PlayClipSetsKindAndSlot()
        {
            var command = WallCommand.PlayClip(7);

            Assert.Equal(CommandKind.PlayClip, command.Kind);
            Assert.Equal(7, command.Slot);
        }

        [Fact]
        public void SimpleSetsOnlyTheKind()
        {
            var command = WallCommand.Simple(CommandKind.Stop);

            Assert.Equal(CommandKind.Stop, command.Kind);
        }

        [Fact]
        public void WithValueSetsKindAndValue()
        {
            var command = WallCommand.WithValue(CommandKind.Brightness, 0.5f);

            Assert.Equal(CommandKind.Brightness, command.Kind);
            Assert.Equal(0.5f, command.Value);
        }

        [Fact]
        public void FitSetsKindAndEncodesModeInValue()
        {
            Assert.Equal(CommandKind.Fit, WallCommand.Fit(FitMode.Stretch).Kind);
            Assert.Equal(1f, WallCommand.Fit(FitMode.Stretch).Value);
            Assert.Equal(0f, WallCommand.Fit(FitMode.Crop).Value);
        }
    }
}
