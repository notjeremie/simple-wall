using System;

namespace SimpleWall.Engine
{
    /// <summary>
    /// The legal range for brightness and contrast, in ONE place.
    ///
    /// It lives here because every component that touches these values has re-derived the same
    /// clamp and got it subtly different, and the same trap has now caught three of them:
    /// Math.Min/Max PROPAGATE NaN rather than clamping it, so the obvious
    /// Math.Max(0, Math.Min(2, v)) is not a clamp at all for the one input that matters.
    ///
    /// A value out of range is never a reason to fail. It is a reason to pick something sane and
    /// keep the wall lit.
    /// </summary>
    public static class AdjustValue
    {
        public const float Min = 0f;
        public const float Max = 2f;

        /// <summary>What an unusable value becomes. Not 0 -- a black wall is not a safe default.</summary>
        public const float Neutral = 1f;

        /// <summary>
        /// NaN maps to <see cref="Neutral"/>; infinities clamp to the ends like any other
        /// out-of-range number. Note float can reach infinity from an ordinary-looking decimal:
        /// a config holding 1e40 overflows on parse, and config.json is deliberately NOT
        /// range-validated on load.
        /// </summary>
        public static float Clamp(float value) =>
            float.IsNaN(value) ? Neutral : Math.Max(Min, Math.Min(Max, value));
    }
}
