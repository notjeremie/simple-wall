using System;
using System.Globalization;
using SimpleWall.Engine;

namespace SimpleWall.Osc
{
    public static class OscParser
    {
        private const string ClipPrefix = "/clip/";

        /// <summary>Returns null for anything that should be ignored — unknown, malformed, or a button release.</summary>
        public static WallCommand Parse(string address, object[] arguments)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            if (address.StartsWith(ClipPrefix, StringComparison.Ordinal))
                return Trigger(ParseClip(address), arguments);

            switch (address)
            {
                case "/play":       return Trigger(WallCommand.Simple(CommandKind.Play), arguments);
                case "/pause":      return Trigger(WallCommand.Simple(CommandKind.Pause), arguments);
                case "/toggle":     return Trigger(WallCommand.Simple(CommandKind.Toggle), arguments);
                case "/stop":       return Trigger(WallCommand.Simple(CommandKind.Stop), arguments);
                case "/brightness": return Adjust(CommandKind.Brightness, arguments);
                case "/contrast":   return Adjust(CommandKind.Contrast, arguments);
                default:            return null;
            }
        }

        private static WallCommand ParseClip(string address)
        {
            var raw = address.Substring(ClipPrefix.Length);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) || slot < 1)
                return null;
            return WallCommand.PlayClip(slot);
        }

        /// <summary>
        /// Triggers take no value, so a leading 0 can only be a button release and must not re-fire.
        /// Addresses that carry a value never come through here — for them 0 is data, not a release.
        /// </summary>
        private static WallCommand Trigger(WallCommand command, object[] arguments) =>
            IsButtonRelease(arguments) ? null : command;

        private static bool IsButtonRelease(object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return false;
            return TryFloat(arguments[0], out var value) && value == 0f;
        }

        private static WallCommand Adjust(CommandKind kind, object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return null;
            if (!TryFloat(arguments[0], out var value)) return null;
            // Math.Min/Max propagate NaN rather than clamping it, so NaN would escape the range
            // and reach a native VLC filter parameter. It isn't a value — treat it as malformed.
            if (float.IsNaN(value)) return null;
            return WallCommand.WithValue(kind, Math.Max(0f, Math.Min(2f, value)));
        }

        private static bool TryFloat(object argument, out float value)
        {
            value = 0f;
            switch (argument)
            {
                case float f: value = f; return true;
                case int i: value = i; return true;
                case double d: value = (float)d; return true;
                default: return false;
            }
        }
    }
}
