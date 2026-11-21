using HarmonyLib;
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
            if (!WorldspaceIconManager.HasInstance) return null;
            WorldspaceIconUiSettings settings = IconSettings(icon);
            if (settings == null) return null;
            if (settings == WorldspaceIconManager.HoleIconSettings) return HudPart.HoleMarker;
            if (settings == WorldspaceIconManager.BallIconSettings) return HudPart.BallMarker;
            if (settings == WorldspaceIconManager.TeammateIconSettings) return HudPart.TeammateMarkers;
            if (settings == WorldspaceIconManager.ObjectiveIconSettings || settings == WorldspaceIconManager.BallDispenserIconSettings ||
                settings == WorldspaceIconManager.HomingWarningIconSettings) return HudPart.OtherMarkers;
            return null;
        }

        private static readonly AccessTools.FieldRef<WorldspaceIconUi, WorldspaceIconUiSettings> IconSettings =
            AccessTools.FieldRefAccess<WorldspaceIconUi, WorldspaceIconUiSettings>("settings");

        /// <summary>The markers: the game sizes them by their rectangle and keeps their scale at 1; this sets the scale.</summary>
        [HarmonyPatch(typeof(WorldspaceIconUi), nameof(WorldspaceIconUi.OnPostPostLateBUpdate))]
        internal static class MarkerPatch
        {
            private static void Postfix(WorldspaceIconUi __instance)
            {
                HudPart? part = MarkerPart(__instance);
                if (part == null) return;
                float k = HudScaler.Scale(part.Value);
                Transform transform = __instance.transform;
                if (transform.localScale.x != k || transform.localScale.y != k) transform.localScale = new Vector3(k, k, k);
            }
        }

        /// <summary>The name tags: the game sets their scale from their distance; this scales that.</summary>
        [HarmonyPatch(typeof(NameTagUi), "<OnPostPostLateBUpdate>g__UpdateSize|23_1")]
        internal static class NameTagPatch
        {
            private static void Postfix(NameTagUi __instance)
            {
                float k = HudScaler.Scale(HudPart.NameTags);
                if (k != 1f) __instance.transform.localScale *= k;
            }
        }

        /// <summary>The pop-up words: the game sets their scale from their distance and pop; this scales that.</summary>
        [HarmonyPatch(typeof(TextPopupUi), "UpdateSize")]
        internal static class PopupPatch
        {
            private static void Postfix(TextPopupUi __instance)
            {
                float k = HudScaler.Scale(HudPart.PopupText);
                if (k != 1f) __instance.transform.localScale *= k;
            }
        }
    }
}
