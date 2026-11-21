using System.Collections.Generic;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Balls glow in the dark, so they stay easy to find at night: each ball in play glows evenly all over in its own
    /// colours, the player's own the brightest. No light is cast on the ground (a light at the ball made a bright pool
    /// on the grass round it). The glow follows how dark it is (the time of day's night setting, or a dim sun on a hole
    /// the game lights for night itself) and is off in daylight.
    ///
    /// <para>The even glow is the game's own toon shader emission: the ball's material gets the emissive keyword (only
    /// when the game's build has that variant and the material's own emission colour is black, so nothing else using
    /// it changes), and each ball's renderer gets its emission colour and texture (the ball's base colour and texture)
    /// through a property block of its own, so the own ball and others can glow differently. Balls drawn by another
    /// shader don't glow.</para>
    /// </summary>
    internal sealed class BallGlow : IFeature
    {
        private readonly EnvironmentController _environment;

        internal BallGlow(EnvironmentController environment) => _environment = environment;

        public string Name => "Ball glow";

        /// <summary>Emission at strength 1, fully dark, times the ball's own colour: own ball and others.</summary>
        private const float OwnGlow = 0.75f, OtherGlow = 0.5f;

        private static readonly int EmissiveColorId = Shader.PropertyToID(ToonEmissive.ColorProperty);
        private static readonly int EmissiveTexId = Shader.PropertyToID(ToonEmissive.TextureProperty);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private readonly MaterialEdits _edits = new MaterialEdits();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        /// <summary>Renderers given an emission, and whether they had a property block of the game's before.</summary>
        private readonly Dictionary<Renderer, bool> _glowing = new Dictionary<Renderer, bool>();
        private readonly HashSet<Renderer> _thisFrame = new HashSet<Renderer>();
        private readonly List<Renderer> _done = new List<Renderer>();
        private readonly HashSet<Material> _cantGlow = new HashSet<Material>();

        private GolfBall[] _balls = System.Array.Empty<GolfBall>();
        private float _nextScan;

        public void Apply()
        {
            float strength = Settings.BallGlow.Value;
            float dark = _environment.Darkness;
            if (!GameEvents.InHole || strength <= 0f || dark <= 0.01f)
            {
                Restore();
                return;
            }

            // Balls come and go with players; look for them about once a second.
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                _balls = Object.FindObjectsByType<GolfBall>(FindObjectsSortMode.None);
            }

            bool ownOnly = Settings.BallGlowOwnOnly.Value;
            _thisFrame.Clear();
            foreach (GolfBall ball in _balls)
            {
                if (ball == null || !ball.isActiveAndEnabled || ball.IsHidden) continue;
                bool own = ball.Owner != null && ball.Owner.isLocalPlayer;
                if (ownOnly && !own) continue;

                float glow = (own ? OwnGlow : OtherGlow) * strength * dark;
                foreach (Renderer r in ball.GetComponentsInChildren<Renderer>())
                    if (Glow(r, glow)) _thisFrame.Add(r);
            }

            // Balls gone, hidden or no longer wanted: their renderers back as they were.
            _done.Clear();
            foreach (Renderer r in _glowing.Keys)
                if (!_thisFrame.Contains(r)) _done.Add(r);
            foreach (Renderer r in _done) Unglow(r);
        }

        /// <summary>Gives the renderer an even glow of its own colours. False if its material can't glow.</summary>
        private bool Glow(Renderer r, float level)
        {
            if (r == null || !(r is MeshRenderer)) return false;
            Material m = r.sharedMaterial;
            if (!ToonEmissive.IsToon(m) || _cantGlow.Contains(m)) return false;

            if (!m.IsKeywordEnabled(ToonEmissive.Keyword))
            {
                Color stockEmission = m.HasProperty(EmissiveColorId) ? _edits.StockColor(m, ToonEmissive.ColorProperty) : Color.black;
                if (!ToonEmissive.HasVariant(m) || stockEmission.maxColorComponent > 0.001f)
                {
                    _cantGlow.Add(m);
                    Plugin.LogChange("ballglow:" + m.name,
                        $"Ball glow: {m.name} doesn't glow (it can't be switched to glow without changing how it's drawn).");
                    return false;
                }
                _edits.SetKeyword(m, ToonEmissive.Keyword, true);
                if (m.HasProperty(ToonEmissive.Toggle)) _edits.SetFloat(m, ToonEmissive.Toggle, 1f);
            }

            Color baseColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            Texture main = m.HasProperty(MainTexId) ? m.GetTexture(MainTexId) : null;

            if (!_glowing.ContainsKey(r)) _glowing[r] = r.HasPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetColor(EmissiveColorId, new Color(baseColor.r * level, baseColor.g * level, baseColor.b * level, 1f));
            _block.SetTexture(EmissiveTexId, main != null ? main : Texture2D.whiteTexture);
            r.SetPropertyBlock(_block);
            return true;
        }

        /// <summary>The renderer's emission back to its material's own (or its property block removed, if it had none).</summary>
        private void Unglow(Renderer r)
        {
            if (!_glowing.TryGetValue(r, out bool hadBlock)) return;
            _glowing.Remove(r);
            if (r == null) return;
            if (!hadBlock)
            {
                r.SetPropertyBlock(null);
                return;
            }
            Material m = r.sharedMaterial;
            r.GetPropertyBlock(_block);
            _block.SetColor(EmissiveColorId, m != null && m.HasProperty(EmissiveColorId) ? m.GetColor(EmissiveColorId) : Color.black);
            Texture tex = m != null && m.HasProperty(EmissiveTexId) ? m.GetTexture(EmissiveTexId) : null;
            _block.SetTexture(EmissiveTexId, tex != null ? tex : Texture2D.whiteTexture);
            r.SetPropertyBlock(_block);
        }

        public void Restore()
        {
            if (_glowing.Count > 0)
            {
                _done.Clear();
                _done.AddRange(_glowing.Keys);
                foreach (Renderer r in _done) Unglow(r);
            }
            _edits.Restore();
            // Let go of every renderer and material, so nothing from a map that's gone stays referenced (the
            // renderer list kept the last frame's balls while the glow was off). Also called at each map change.
            _glowing.Clear();
            _thisFrame.Clear();
            _done.Clear();
            _cantGlow.Clear();
            _balls = System.Array.Empty<GolfBall>();
            _nextScan = 0f;
        }
    }
}
