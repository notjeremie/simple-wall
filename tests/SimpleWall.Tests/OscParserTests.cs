using Xunit;
using SimpleWall.Engine;
using SimpleWall.Osc;

namespace SimpleWall.Tests
{
    public class OscParserTests
    {
        [Theory]
        [InlineData("/play", CommandKind.Play)]
        [InlineData("/pause", CommandKind.Pause)]
        [InlineData("/toggle", CommandKind.Toggle)]
        [InlineData("/stop", CommandKind.Stop)]
        public void BareTransportAddressesParse(string address, CommandKind expected)
        {
            Assert.Equal(expected, OscParser.Parse(address, new object[0]).Kind);
        }

        [Fact]
        public void ClipAddressCarriesItsSlot()
        {
            var command = OscParser.Parse("/clip/7", new object[0]);

            Assert.Equal(CommandKind.PlayClip, command.Kind);
            Assert.Equal(7, command.Slot);
        }

        [Fact]
        public void ButtonPressValueIsAccepted()
        {
            // some Stream Deck OSC plugins send 1 on press
            Assert.NotNull(OscParser.Parse("/clip/7", new object[] { 1f }));
        }

        [Fact]
        public void ButtonReleaseValueIsIgnored()
        {
            // ...and 0 on release — which must not re-trigger
            Assert.Null(OscParser.Parse("/clip/7", new object[] { 0f }));
        }

        [Fact]
        public void TransportButtonReleaseIsIgnored()
        {
            Assert.Null(OscParser.Parse("/play", new object[] { 0f }));
            Assert.Null(OscParser.Parse("/stop", new object[] { 0f }));
        }

        [Fact]
        public void BrightnessCarriesItsValue()
        {
            var command = OscParser.Parse("/brightness", new object[] { 0.5f });

            Assert.Equal(CommandKind.Brightness, command.Kind);
            Assert.Equal(0.5f, command.Value);
        }

        [Fact]
        public void BrightnessZeroIsABlackoutNotAButtonRelease()
        {
            // /brightness 0 is a legitimate command. The release guard must not swallow it.
            var command = OscParser.Parse("/brightness", new object[] { 0f });

            Assert.NotNull(command);
            Assert.Equal(CommandKind.Brightness, command.Kind);
            Assert.Equal(0f, command.Value);
        }

        [Fact]
        public void ContrastZeroIsAlsoNotAButtonRelease()
        {
            var command = OscParser.Parse("/contrast", new object[] { 0f });

            Assert.NotNull(command);
            Assert.Equal(CommandKind.Contrast, command.Kind);
            Assert.Equal(0f, command.Value);
        }

        [Fact]
        public void BrightnessIsClampedToRange()
        {
            Assert.Equal(2f, OscParser.Parse("/brightness", new object[] { 99f }).Value);
            Assert.Equal(0f, OscParser.Parse("/brightness", new object[] { -5f }).Value);
        }

        [Fact]
        public void NaNIsIgnoredRatherThanEscapingTheClamp()
        {
            // Math.Min/Max propagate NaN instead of clamping it, so a NaN would otherwise
            // reach the engine as a brightness value.
            Assert.Null(OscParser.Parse("/brightness", new object[] { float.NaN }));
            Assert.Null(OscParser.Parse("/contrast", new object[] { float.NaN }));
            Assert.Null(OscParser.Parse("/brightness", new object[] { double.NaN }));
        }

        [Fact]
        public void InfinitiesAreClampedToTheEndsOfTheRange()
        {
            Assert.Equal(2f, OscParser.Parse("/brightness", new object[] { float.PositiveInfinity }).Value);
            Assert.Equal(0f, OscParser.Parse("/brightness", new object[] { float.NegativeInfinity }).Value);
        }

        [Fact]
        public void AddressMatchingIsCaseSensitive()
        {
            Assert.Null(OscParser.Parse("/PLAY", new object[0]));
            Assert.Null(OscParser.Parse("/CLIP/7", new object[0]));
        }

        [Fact]
        public void ClipAddressWithTrailingSegmentsIsIgnored()
        {
            Assert.Null(OscParser.Parse("/clip/7/extra", new object[0]));
        }

        [Fact]
        public void HugeClipSlotIsIgnoredRatherThanOverflowing()
        {
            Assert.Null(OscParser.Parse("/clip/99999999999999", new object[0]));
        }

        [Fact]
        public void AdjustmentAcceptsIntAndDoubleArguments()
        {
            Assert.Equal(1f, OscParser.Parse("/brightness", new object[] { 1 }).Value);
            Assert.Equal(0.25f, OscParser.Parse("/contrast", new object[] { 0.25d }).Value);
        }

        [Fact]
        public void AdjustmentWithoutAValueIsIgnored()
        {
            Assert.Null(OscParser.Parse("/brightness", new object[0]));
            Assert.Null(OscParser.Parse("/contrast", new object[] { "loud" }));
        }

        [Fact]
        public void UnknownAddressIsIgnored()
        {
            Assert.Null(OscParser.Parse("/nonsense", new object[0]));
        }

        [Fact]
        public void MalformedClipAddressIsIgnored()
        {
            Assert.Null(OscParser.Parse("/clip/abc", new object[0]));
            Assert.Null(OscParser.Parse("/clip/", new object[0]));
        }

        [Fact]
        public void NonPositiveClipSlotIsIgnored()
        {
            Assert.Null(OscParser.Parse("/clip/0", new object[0]));
            Assert.Null(OscParser.Parse("/clip/-3", new object[0]));
        }

        [Fact]
        public void EmptyOrNullAddressIsIgnored()
        {
            Assert.Null(OscParser.Parse(null, new object[0]));
            Assert.Null(OscParser.Parse("", new object[0]));
            Assert.Null(OscParser.Parse("   ", new object[0]));
        }

        [Fact]
        public void NullArgumentsAreTreatedAsNoArguments()
        {
            // the network feeds this parser — a null argument array must not throw
            Assert.Equal(CommandKind.Play, OscParser.Parse("/play", null).Kind);
            Assert.Null(OscParser.Parse("/brightness", null));
        }
    }
}
