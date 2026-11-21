using System;

namespace LightGreen.Core
{
    /// <summary>
    /// The arithmetic of HUD scaling, kept free of Unity so it can be checked on its own. An element shrinks toward the
    /// side of the screen it's anchored to: one anchored left keeps its left edge where it is, one anchored to the
    /// bottom right keeps its bottom-right corner, one anchored in the middle keeps its centre.
    /// </summary>
    internal static class HudMath
    {
        /// <summary>
        /// The scale used for 0 %: too small to see, but not zero, so the game's own maths on the element (a point
        /// converted into its space, say) never divides by zero.
        /// </summary>
        internal const float Hidden = 1e-4f;

        /// <summary>A setting's percentage as a scale: 100 → 1, 50 → 0.5, 0 → <see cref="Hidden"/>.</summary>
        internal static float Scale(int percent)
        {
            if (percent >= 100) return 1f;
            if (percent <= 0) return Hidden;
            return percent / 100f;
        }

        /// <summary>
        /// Where along one axis of its own rectangle an element stays still as it shrinks, from its anchors on that
        /// axis: 0 = its left (or bottom) edge, 0.5 = its centre, 1 = its right (or top) edge.
        /// </summary>
        internal static float Edge(float anchorMin, float anchorMax)
        {
            float anchor = (anchorMin + anchorMax) * 0.5f;
            if (anchor < 0.34f) return 0f;
            if (anchor > 0.66f) return 1f;
            return 0.5f;
        }

        /// <summary>Whether an element stretches across its parent on one axis (anchors at both ends).</summary>
        internal static bool Stretched(float anchorMin, float anchorMax) => anchorMax - anchorMin > 0.9f;

        /// <summary>A coordinate moved toward a still point as its element is scaled by k about that point.</summary>
        internal static float Toward(float value, float still, float k) => still + (value - still) * k;

        /// <summary>Whether two coordinates are the same, give or take float rounding.</summary>
        internal static bool Same(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));
    }
}
