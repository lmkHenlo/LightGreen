using System;
using System.Reflection;
using HarmonyLib;
using LightGreen.Core;

namespace LightGreen.Systems
{
    /// <summary>
    /// Shading.Grass = false hides the game's grass blades. The game draws them with one instanced draw per terrain
    /// and level of detail (TerrainGrassRenderer); while hidden, that draw is skipped and nothing else changes: the
    /// game still works out which blades to draw and frees that work as usual, so drawing them again is immediate.
    /// The terrain's own colour shows where the blades were. The grass comes back with LightGreen off (ToggleKey)
    /// or outside a hole.
    /// </summary>
    internal sealed class GrassToggle : IFeature
    {
        private static bool _hidden;

        /// <summary>Set by Plugin when the hook applied.</summary>
        internal static bool Hooked;

        public string Name => "Grass";

        private static bool _settingLogged;

        public void Apply()
        {
            bool off = !Settings.Grass.Value;
            // Logged when the setting takes effect, not each time the ToggleKey or a new hole brings it back.
            if (Hooked && off != _settingLogged)
            {
                _settingLogged = off;
                Plugin.Log.LogInfo(off ? "Grass: hidden (Shading.Grass = false)." : "Grass: drawn again (Shading.Grass = true).");
            }
            _hidden = Hooked && off && GameEvents.InHole;
        }

        public void Restore() => _hidden = false;

        /// <summary>The draw of one level of detail's blades (TerrainGrassRenderer.LodBatchJobData.Render).</summary>
        [HarmonyPatch]
        internal static class DrawPatch
        {
            private static MethodBase TargetMethod()
            {
                Type renderer = AccessTools.TypeByName("TerrainGrassRenderer");
                Type batch = renderer == null ? null : AccessTools.Inner(renderer, "LodBatchJobData");
                return batch == null ? null : AccessTools.Method(batch, "Render");
            }

            private static bool Prefix() => !_hidden;
        }
    }
}
