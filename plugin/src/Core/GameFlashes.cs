using System;

namespace LightGreen.Core
{
    /// <summary>
    /// The game's own "Explosion flashes" option (General settings; it replaced the older "flashing effects"
    /// option). When the player has turned it off, LightGreen's flashes are gentle too: a slower rise, half the
    /// brightness and no flicker.
    /// </summary>
    internal static class GameFlashes
    {
        /// <summary>True when the player turned the game's explosion flashes off. Read each frame; cheap.</summary>
        internal static bool Off
        {
            get
            {
                try
                {
                    GameSettings.GeneralSettings general = GameSettings.All?.General;
                    return general != null && !general.ExplosionFlashes;
                }
                catch (Exception)
                {
                    return false;   // settings not loaded yet: the game's default (flashes on)
                }
            }
        }
    }
}
