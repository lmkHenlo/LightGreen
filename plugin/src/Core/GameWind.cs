using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// The game's wind, as the HUD wind arrow shows it and the ball flies with it: the direction it blows
    /// toward on the ground (world x, z) and its speed in km/h. The same for every player in a match.
    /// Cloud shadows and the sky's clouds both move with it, so they can't disagree.
    /// </summary>
    internal static class GameWind
    {
        /// <summary>The direction the wind blows toward, world x and z, unit length; zero with no wind.</summary>
        internal static Vector2 Direction
        {
            get
            {
                Vector3 d = WindManager.CurrentWindDirection;
                Vector2 flat = new Vector2(d.x, d.z);
                return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.zero;
            }
        }

        /// <summary>Wind speed in km/h (the game's unit); 0 with no wind, or where the game has none.</summary>
        internal static float SpeedKmh => Direction == Vector2.zero ? 0f : Mathf.Max(0f, WindManager.CurrentWindSpeed);
    }
}
