using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace LightGreen.Core
{
    /// <summary>
    /// The log the Core classes write to, set by the plugin at start, so Core doesn't reach up into the plugin. Also
    /// says a failure once: for a fallback that's right for a moment (a scene loading) but would be permanent and
    /// unseen after a game update.
    /// </summary>
    internal static class CoreLog
    {
        /// <summary>BepInEx's log for LightGreen; null only before the plugin starts (and in the offline checks).</summary>
        internal static ManualLogSource Source = null;

        private static readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Logs a warning the first time <paramref name="key"/> fails this session, with the exception in full.</summary>
        internal static void FailedOnce(string key, string what, Exception e)
        {
            if (!Said.Add(key)) return;
            Source?.LogWarning($"{what} ({e.GetType().Name}: {e.Message}). Said once; LightGreen carries on without it.\n{e}");
        }
    }
}
