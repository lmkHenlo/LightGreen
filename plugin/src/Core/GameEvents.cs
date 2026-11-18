using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine.SceneManagement;

namespace LightGreen.Core
{
    /// <summary>
    /// The game events every LightGreen system listens to, collected in one place. Each hook is
    /// attached on its own: if a game update removes one, only that hook is lost and the log says so.
    /// </summary>
    internal static class GameEvents
    {
        /// <summary>A hole scene of another biome (or the menu) was loaded.</summary>
        internal static event Action<Biome> BiomeChanged;

        /// <summary>The match moved to another hole.</summary>
        internal static event Action HoleChanged;

        /// <summary>
        /// The game applied its graphics settings, which may have swapped the render pipeline asset.
        /// Anything LightGreen sets on the pipeline must be set again after this.
        /// </summary>
        internal static event Action GraphicsSettingsApplied;

        /// <summary>The camera switched mode (orbit, overview, photo and so on).</summary>
        internal static event Action CameraModuleChanged;

        internal static Biome CurrentBiome { get; private set; } = Biome.None;

        /// <summary>On a hole (not the menus): what LightGreen's effects need to be on.</summary>
        internal static bool InHole => CurrentBiome != Biome.None;

        private static readonly List<Action> Unhooks = new List<Action>();
        private static ManualLogSource _log;

        internal static void Hook(ManualLogSource log)
        {
            _log = log;
            TryHook("Scenes loaded", () =>
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                Unhooks.Add(() =>
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    SceneManager.sceneUnloaded -= OnSceneUnloaded;
                });
            });
            TryHook("Hole changed", () =>
            {
                CourseManager.CurrentHoleGlobalIndexChanged += OnHoleChanged;
                Unhooks.Add(() => CourseManager.CurrentHoleGlobalIndexChanged -= OnHoleChanged);
            });
            TryHook("Graphics settings applied", () =>
            {
                GameSettings.GraphicsSettings.OnGraphicsQualityApply += OnGraphicsApplied;
                Unhooks.Add(() => GameSettings.GraphicsSettings.OnGraphicsQualityApply -= OnGraphicsApplied);
            });
            TryHook("Camera mode changed", () =>
            {
                CameraModuleController.ModuleTypeChanged += OnCameraModuleChanged;
                Unhooks.Add(() => CameraModuleController.ModuleTypeChanged -= OnCameraModuleChanged);
            });
            UpdateBiome();
        }

        internal static void Unhook()
        {
            foreach (Action unhook in Unhooks)
            {
                try { unhook(); }
                catch { /* the game is shutting down */ }
            }
            Unhooks.Clear();
        }

        // The game's types are only touched inside these lambdas, so a missing type or event throws
        // when the lambda runs (inside the try), not when this class loads.
        private static void TryHook(string label, Action hook)
        {
            try
            {
                hook();
                _log.LogInfo($"[ok] Hook: {label}");
            }
            catch (Exception e)
            {
                _log.LogError($"[FAILED] Hook: {label}: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => UpdateBiome();

        private static void OnSceneUnloaded(Scene scene) => UpdateBiome();

        private static void OnHoleChanged()
        {
            UpdateBiome();
            Raise(HoleChanged, "HoleChanged");
        }

        private static void OnGraphicsApplied() => Raise(GraphicsSettingsApplied, "GraphicsSettingsApplied");

        private static void OnCameraModuleChanged() => Raise(CameraModuleChanged, "CameraModuleChanged");

        private static void UpdateBiome()
        {
            Biome biome = BiomeDetection.FromLoadedScenes();
            if (biome == CurrentBiome) return;
            CurrentBiome = biome;
            Action<Biome> handlers = BiomeChanged;
            if (handlers == null) return;
            foreach (Action<Biome> handler in handlers.GetInvocationList())
            {
                try { handler(biome); }
                catch (Exception e) { _log.LogError($"BiomeChanged handler failed: {e}"); }
            }
        }

        // One failing listener must not stop the others, or the game's own code that raised the event.
        private static void Raise(Action handlers, string name)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { _log.LogError($"{name} handler failed: {e}"); }
            }
        }
    }
}
