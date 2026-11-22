using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// Copies of a particle module's curves and gradients that LightGreen owns. In Unity 6 the getters
    /// (<c>main.startLifetime</c>, <c>main.startColor</c>, <c>colorOverLifetime.color</c>, …) wrap the particle system's
    /// own native curve and gradient without copying them (<c>MinMaxGradientBlittable.ToMinMaxGradient</c> makes
    /// <c>new Gradient(IntPtr)</c>, which doesn't own the pointer). Kept past that system's life, they point at freed
    /// memory, and setting them on another system copies whatever is there now. Anything kept for later goes through
    /// here, while the system it came from still exists.
    /// </summary>
    internal static class ParticleValues
    {
        internal static ParticleSystem.MinMaxCurve Copy(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Curve:
                    return new ParticleSystem.MinMaxCurve(c.curveMultiplier, Copy(c.curveMax));
                case ParticleSystemCurveMode.TwoCurves:
                    return new ParticleSystem.MinMaxCurve(c.curveMultiplier, Copy(c.curveMin), Copy(c.curveMax));
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(c.constantMin, c.constantMax);
                default:
                    return new ParticleSystem.MinMaxCurve(c.constantMax);
            }
        }

        internal static ParticleSystem.MinMaxGradient Copy(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Copy(g.gradientMax));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Copy(g.gradientMin), Copy(g.gradientMax));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(g.colorMin, g.colorMax);
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(Copy(g.gradientMax)) { mode = ParticleSystemGradientMode.RandomColor };
                default:
                    return new ParticleSystem.MinMaxGradient(g.colorMax);
            }
        }

        /// <summary>A new curve with the same keys and wrap modes (an empty one for none).</summary>
        internal static AnimationCurve Copy(AnimationCurve a)
        {
            if (a == null) return new AnimationCurve();
            return new AnimationCurve(a.keys) { preWrapMode = a.preWrapMode, postWrapMode = a.postWrapMode };
        }

        /// <summary>A new gradient with the same keys, blend mode and colour space (an empty one for none).</summary>
        internal static Gradient Copy(Gradient g)
        {
            var copy = new Gradient();
            if (g == null) return copy;
            copy.mode = g.mode;
            copy.colorSpace = g.colorSpace;
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
        }
    }
}
