using System;
using UnityEngine.SceneManagement;

namespace LightGreen.Core
{
    /// <summary>The game's course themes, plus the places that aren't a hole.</summary>
    internal enum Biome
    {
        None,
        DrivingRange,
        Coast,
        Forest,
        Desert,
        Snow,
        City,

        /// <summary>A hole in a folder LightGreen doesn't know, for example after a game update.</summary>
        Unknown,
    }

    internal static class BiomeDetection
    {
        /// <summary>The biome of the first loaded hole scene, or None in the menu.</summary>
        internal static Biome FromLoadedScenes()
        {
            Biome found = Biome.None;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                Biome biome = FromScenePath(scene.path);
                if (biome == Biome.None) continue;
                if (biome != Biome.DrivingRange) return biome;
                found = biome;
            }
            return found;
        }

        /// <summary>
        /// Hole scenes live in a folder per biome, for example "Assets/Scenes/Holes/Coast/Cove.unity".
        /// </summary>
        internal static Biome FromScenePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Biome.None;
            if (path.EndsWith("/Driving range.unity", StringComparison.OrdinalIgnoreCase)) return Biome.DrivingRange;

            const string marker = "/Holes/";
            int start = path.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return Biome.None;
            start += marker.Length;
            int end = path.IndexOf('/', start);
            if (end < 0) return Biome.None;

            switch (path.Substring(start, end - start))
            {
                case "Coast": return Biome.Coast;
                case "Forest": return Biome.Forest;
                case "Desert": return Biome.Desert;
                case "Snow": return Biome.Snow;
                case "City": return Biome.City;
                default: return Biome.Unknown;
            }
        }
    }
}
