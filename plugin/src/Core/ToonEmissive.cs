using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// Whether a game toon material can be switched to glow (the emissive keyword, _EMISSIVEENABLE_ON) and still be
    /// drawn with a variant the game's build has. A material asking for a combination the build doesn't have is drawn
    /// with the closest one it has, which can lose a feature, so City night and ball glow only switch these.
    /// </summary>
    internal static class ToonEmissive
    {
        internal const string ShaderName = "Custom/ToonShader";
        internal const string Keyword = "_EMISSIVEENABLE_ON";
        internal const string Toggle = "_EmissiveEnable";
        internal const string ColorProperty = "_EmissiveColor";
        internal const string TextureProperty = "_EmissiveTex";

        /// <summary>
        /// The toon shader's material-keyword sets (its shader_feature_local keywords, without the ones the game
        /// switches at run time) that the game's build has an emissive variant of, checked against game 1.4.0-798.
        /// The empty set is plain UV mapping, nothing else.
        /// </summary>
        private static readonly string[][] EmissiveBases =
        {
            new string[0],
            new[] { "_USE_1STAS2ND_ON" },
            new[] { "_TINTMASKENABLE_ON", "_USE_1STAS2ND_ON" },
            new[] { "_BLOATENABLE_ON", "_USE_1STAS2ND_ON" },
            new[] { "_ALPHACUTOFFENABLE_ON", "_BLOATENABLE_ON", "_USE_1STAS2ND_ON" },
        };

        /// <summary>The toon shader's material feature keywords (shader_feature_local).</summary>
        private static readonly string[] FeatureKeywords =
        {
            "_UVMODE_WORLDSPACE", "_UVMODE_TRIPLANAR", "_USE_1STAS2ND_ON", "_USENORMALMAP_ON", "_ALPHACUTOFFENABLE_ON",
            "_TINTMASKENABLE_ON", "_BLOATENABLE_ON", "_SNOWENABLE_ON", "_FRESNELENABLE_ON",
        };

        internal static bool IsToon(Material m) => m != null && m.shader != null && m.shader.name == ShaderName;

        /// <summary>True when the build has an emissive variant for this material's features (see <see cref="EmissiveBases"/>).</summary>
        internal static bool HasVariant(Material m)
        {
            var features = new HashSet<string>(m.shaderKeywords.Where(k => FeatureKeywords.Contains(k)));
            return EmissiveBases.Any(b => features.SetEquals(b));
        }
    }
}
