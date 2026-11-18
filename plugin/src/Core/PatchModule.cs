using System;
using BepInEx.Logging;
using HarmonyLib;

namespace LightGreen.Core
{
    /// <summary>
    /// One Harmony patch class and the name it's logged under. Each effect applies its own patches,
    /// so a patch that stops matching after a game update turns off only that effect.
    /// </summary>
    internal sealed class PatchModule
    {
        internal readonly Type Type;
        internal readonly string Label;

        internal PatchModule(Type type, string label)
        {
            Type = type;
            Label = label;
        }

        /// <returns>True if the patch was applied.</returns>
        internal bool TryApply(Harmony harmony, ManualLogSource log)
        {
            try
            {
                var patched = harmony.CreateClassProcessor(Type).Patch();
                if (patched == null || patched.Count == 0)
                {
                    log.LogError($"[FAILED] {Label}: no game method to patch");
                    return false;
                }
                log.LogInfo($"[ok] {Label}");
                return true;
            }
            catch (Exception e)
            {
                log.LogError($"[FAILED] {Label}: {e}");
                return false;
            }
        }
    }
}
