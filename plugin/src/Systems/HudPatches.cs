using System;
using System.Reflection;
using HarmonyLib;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// The HUD pieces the game sizes every frame from their distance to the camera, scaled right after it does:
    /// the markers over the hole, balls, teammates and objectives, the name tags, and the pop-up words. Each patch
    /// only multiplies what the game has just set (or sets the marker's scale the game keeps at 1), so with the
    /// [HUD] sizes off they change nothing.
    /// </summary>
    internal static class HudPatches
    {
        /// <summary>Which [HUD] group a marker belongs to, from the game's settings for its kind; null for others.</summary>
        private static HudPart? MarkerPart(WorldspaceIconUi icon)
        {
            if (!WorldspaceIconManager.HasInstance || IconSettings == null) return null;
            WorldspaceIconUiSettings settings = IconSettings(icon);
            if (settings == null) return null;
            if (settings == WorldspaceIconManager.HoleIconSettings) return HudPart.HoleMarker;
            if (settings == WorldspaceIconManager.BallIconSettings) return HudPart.BallMarker;
            if (settings == WorldspaceIconManager.TeammateIconSettings) return HudPart.TeammateMarkers;
            if (settings == WorldspaceIconManager.ObjectiveIconSettings || settings == WorldspaceIconManager.BallDispenserIconSettings ||
                settings == WorldspaceIconManager.HomingWarningIconSettings) return HudPart.OtherMarkers;
            return null;
        }

        // Looked up without throwing: a game update that renames the field leaves the markers at the game's size
        // (and says so once), rather than failing this class's static setup and every marker with it.
        private static readonly AccessTools.FieldRef<WorldspaceIconUi, WorldspaceIconUiSettings> IconSettings = FindIconSettings();

        private static AccessTools.FieldRef<WorldspaceIconUi, WorldspaceIconUiSettings> FindIconSettings()
        {
            FieldInfo field = AccessTools.Field(typeof(WorldspaceIconUi), "settings");
            if (field != null && typeof(WorldspaceIconUiSettings).IsAssignableFrom(field.FieldType))
                return AccessTools.FieldRefAccess<WorldspaceIconUi, WorldspaceIconUiSettings>(field);
            CoreLog.Source?.LogWarning("HUD: WorldspaceIconUi.settings isn't in this game version; markers keep the game's size.");
            return null;
        }

        private static readonly PatchGuard MarkerGuard = new PatchGuard("HUD markers");
        private static readonly PatchGuard NameTagGuard = new PatchGuard("HUD name tags");
        private static readonly PatchGuard PopupGuard = new PatchGuard("HUD pop-up words");

        /// <summary>The markers: the game sizes them by their rectangle and keeps their scale at 1; this sets the scale.</summary>
        [HarmonyPatch(typeof(WorldspaceIconUi), nameof(WorldspaceIconUi.OnPostPostLateBUpdate))]
        internal static class MarkerPatch
        {
            private static void Postfix(WorldspaceIconUi __instance)
            {
                if (MarkerGuard.Broken) return;
                try
                {
                    HudPart? part = MarkerPart(__instance);
                    if (part == null) return;
                    float k = HudScaler.Scale(part.Value);
                    Transform transform = __instance.transform;
                    if (transform.localScale.x != k || transform.localScale.y != k) transform.localScale = new Vector3(k, k, k);
                }
                catch (Exception e)
                {
                    MarkerGuard.Fail(e);
                }
            }
        }

        /// <summary>
        /// The name tags: the game sets their scale from their distance, in a local function of
        /// OnPostPostLateBUpdate; this scales that. The compiler numbers local functions (<c>|23_1</c>), and the
        /// number changes when the game adds a method, so the function is found by its name without the number.
        /// Only that function is patched: the whole of OnPostPostLateBUpdate doesn't always set the scale, and
        /// scaling a scale the game didn't set that frame would compound it.
        /// </summary>
        [HarmonyPatch]
        internal static class NameTagPatch
        {
            private const string LocalFunction = "<" + nameof(NameTagUi.OnPostPostLateBUpdate) + ">g__UpdateSize|";

            internal static MethodBase Target()
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(NameTagUi)))
                    if (!method.IsStatic && method.Name.StartsWith(LocalFunction, StringComparison.Ordinal)) return method;
                return null;
            }

            // No such function in this game version: Harmony then patches nothing, and the hook is logged as failed.
            private static bool Prepare() => Target() != null;

            private static MethodBase TargetMethod() => Target();

            private static void Postfix(NameTagUi __instance)
            {
                if (NameTagGuard.Broken) return;
                try
                {
                    float k = HudScaler.Scale(HudPart.NameTags);
                    if (k != 1f) __instance.transform.localScale *= k;
                }
                catch (Exception e)
                {
                    NameTagGuard.Fail(e);
                }
            }
        }

        /// <summary>The pop-up words: the game sets their scale from their distance and pop; this scales that.</summary>
        [HarmonyPatch(typeof(TextPopupUi), "UpdateSize")]
        internal static class PopupPatch
        {
            private static void Postfix(TextPopupUi __instance)
            {
                if (PopupGuard.Broken) return;
                try
                {
                    float k = HudScaler.Scale(HudPart.PopupText);
                    if (k != 1f) __instance.transform.localScale *= k;
                }
                catch (Exception e)
                {
                    PopupGuard.Fail(e);
                }
            }
        }
    }
}
