using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// Random numbers for LightGreen's own looks. Not UnityEngine.Random: that sequence is the game's, and every
    /// number LightGreen took from it would move what the game gets next. Main thread only.
    /// </summary>
    internal static class LocalRandom
    {
        private static readonly System.Random Source = new System.Random();

        /// <summary>0 (included) to 1 (not included).</summary>
        internal static float Value => (float)Source.NextDouble();

        /// <summary>From <paramref name="min"/> to <paramref name="max"/>.</summary>
        internal static float Range(float min, float max) => min + (max - min) * Value;

        /// <summary>A point inside the circle of radius 1, every point as likely.</summary>
        internal static Vector2 InsideUnitCircle
        {
            get
            {
                float angle = Value * 2f * Mathf.PI;
                float radius = Mathf.Sqrt(Value);
                return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }
        }
    }
}
