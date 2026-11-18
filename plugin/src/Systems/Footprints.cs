using System;
using System.Collections.Generic;
using HarmonyLib;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Longer-lasting footprints in snow. The game already leaves a footprint (VfxType.CharacterFootprint)
    /// on terrain that takes them; it fades after about two seconds and is a faint brown. On snow terrain
    /// this keeps each print for Effects.FootprintSeconds, in a cool blue-grey shadow tone that reads on
    /// white, and fades it out over the last fifth of its life. Sand and other ground keep the game's
    /// prints.
    /// <para>
    /// Trails pile up with many players (BigLobby), so there is a cap (Effects.FootprintMax): past it the
    /// oldest prints fade out over a second. Prints are cleared when the hole changes. A player hidden by
    /// a smoke bomb from you keeps the game's short prints, so a longer trail never gives them away.
    /// </para>
    /// <para>
    /// How: a prefix on PlayerMovementVfx.ShowFootprint notes which ground the step is on; a prefix on the
    /// game's effect-playing method adds a callback for footprint effects that sets the print's lifetime,
    /// colour and fade before it plays. Prints that aren't on snow (or with the effect off) get the game's
    /// own values back, since the effects are pooled and reused. On in VR.
    /// </para>
    /// </summary>
    internal sealed class Footprints : IFeature
    {
        public string Name => "Footprints";

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            Update(Settings.Footprints.Value, Settings.FootprintSeconds.Value, Settings.FootprintMax.Value);
        }

        /// <summary>Fraction of a print's life it stays fully visible; it fades over the rest.</summary>
        private const float Hold = 0.8f;
        /// <summary>How long a print trimmed by the cap, or by turning the effect off, takes to fade.</summary>
        private const float TrimFadeSeconds = 1f;
        /// <summary>The snow print tone: the blue-grey of shadow in a dent in snow.</summary>
        private static readonly Color SnowShade = new Color(0.36f, 0.43f, 0.56f);

        private struct Original
        {
            internal ParticleSystem.MinMaxCurve Lifetime;
            internal ParticleSystem.MinMaxGradient StartColor;
            internal bool FadeEnabled;
            internal ParticleSystem.MinMaxGradient Fade;
        }

        private struct Entry
        {
            internal PoolableParticleSystem Effect;
            internal int Id;
            internal int Generation;
        }

        // Read by the patches; set each frame by Update.
        private static bool _hooked;        // the callback is added once the effect has run at all
        private static bool _active;
        private static float _seconds = 45f;
        private static Color _snowColor = SnowShade;
        private static bool _step;          // inside ShowFootprint, on snow, for a player you can see
        private static readonly Gradient HoldGradient = MakeHoldGradient();
        private static readonly Action<PoolableParticleSystem> Callback = Configure;

        private static List<Original> _originals;
        private static readonly List<ParticleSystem> Children = new List<ParticleSystem>();
        private static readonly Queue<Entry> Live = new Queue<Entry>();
        private static readonly Dictionary<int, int> Generations = new Dictionary<int, int>();
        private static readonly ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[8];
        private static bool _loggedFirst;
        private static bool _loggedCap;

        /// <summary>Notes whether this step is a snow step for a player the local player can see.</summary>
        [HarmonyPatch(typeof(PlayerMovementVfx), "ShowFootprint")]
        internal static class StepPatch
        {
            private static void Prefix(PlayerVfx ___playerVfx)
            {
                _step = false;
                if (!_active) return;
                try
                {
                    PlayerInfo player = ___playerVfx != null ? ___playerVfx.PlayerInfo : null;
                    if (player == null || player.Movement == null) return;
                    PlayerInfo local = GameManager.LocalPlayerInfo;
                    if (local != null && local != player && !local.CanDetectSmokeBombInvisiblePlayer(player)) return;
                    _step = IsSnow(player.Movement.GroundTerrainDominantGlobalLayer);
                }
                catch
                {
                    _step = false;
                }
            }

            private static Exception Finalizer(Exception __exception)
            {
                _step = false;
                return __exception;
            }
        }

        /// <summary>Adds the footprint callback to the game's one effect-playing method.</summary>
        [HarmonyPatch(typeof(VfxManager), "PlayPooledVfxLocalOnlyInternal",
            new[] { typeof(VfxType), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Transform), typeof(bool), typeof(float), typeof(Action<PoolableParticleSystem>) })]
        internal static class EffectPatch
        {
            private static void Prefix(VfxType vfxType, ref Action<PoolableParticleSystem> additionalVfxCallback)
            {
                if (!_hooked || vfxType != VfxType.CharacterFootprint) return;
                additionalVfxCallback = additionalVfxCallback == null ? Callback : additionalVfxCallback + Callback;
            }
        }

        private static bool IsSnow(global::TerrainLayer layer) =>
            layer == global::TerrainLayer.SnowFairway || layer == global::TerrainLayer.SnowGreen ||
            layer == global::TerrainLayer.SnowRough || layer == global::TerrainLayer.SnowOutOfBounds;

        private static Gradient MakeHoldGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, Hold), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Runs just before a footprint effect plays: snow look on snow steps, the game's otherwise.</summary>
        private static void Configure(PoolableParticleSystem effect)
        {
            try
            {
                if (effect == null) return;
                bool snow = _active && _step;
                // Every play counts as a new use, so a tracked snow print that came back as a sand print is let go.
                int id = effect.GetInstanceID();
                Generations.TryGetValue(id, out int generation);
                Generations[id] = ++generation;
                GetPrints(effect);
                if (_originals == null) Capture();
                for (int i = 0; i < Children.Count && i < _originals.Count; i++)
                {
                    ParticleSystem ps = Children[i];
                    Original o = _originals[i];
                    ParticleSystem.MainModule main = ps.main;
                    ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                    if (snow)
                    {
                        main.startLifetime = _seconds;
                        main.startColor = _snowColor;
                        fade.enabled = true;
                        fade.color = HoldGradient;
                    }
                    else
                    {
                        main.startLifetime = o.Lifetime;
                        main.startColor = o.StartColor;
                        fade.enabled = o.FadeEnabled;
                        fade.color = o.Fade;
                    }
                }
                if (snow) Track(effect, id, generation);
            }
            catch
            {
                // Never let a footprint break the game's effect.
            }
        }

        /// <summary>The effect's print particle systems (its children; the root one draws nothing).</summary>
        private static void GetPrints(PoolableParticleSystem effect)
        {
            Children.Clear();
            effect.GetComponentsInChildren(true, Children);
            ParticleSystem root = effect.Particles;
            Children.RemoveAll(ps => ps == root);
        }

        /// <summary>The game's own values, from the first print seen (every print comes from one prefab).</summary>
        private static void Capture()
        {
            _originals = new List<Original>();
            foreach (ParticleSystem ps in Children)
            {
                ParticleSystem.MainModule main = ps.main;
                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                _originals.Add(new Original
                {
                    Lifetime = main.startLifetime,
                    StartColor = main.startColor,
                    FadeEnabled = fade.enabled,
                    Fade = fade.color,
                });
            }
        }

        private static void Track(PoolableParticleSystem effect, int id, int generation)
        {
            Live.Enqueue(new Entry { Effect = effect, Id = id, Generation = generation });
            if (!_loggedFirst)
            {
                _loggedFirst = true;
                Plugin.LogChange("hole:footprints", $"Footprints: snow prints on this hole last {_seconds:0} s (up to {Settings.FootprintMax.Value}).");
            }
        }

        /// <param name="contrast">How dark the snow prints are; 0 = the game's prints.</param>
        /// <param name="seconds">How long a snow print lasts.</param>
        /// <param name="max">How many snow prints there can be at once.</param>
        private void Update(float contrast, float seconds, int max)
        {
            if (contrast <= 0f)
            {
                Restore();
                return;
            }
            _hooked = true;
            _active = true;
            _seconds = Mathf.Max(1f, seconds);
            float t = Mathf.Clamp01(contrast);
            _snowColor = Color.Lerp(new Color(0.15f, 0.07f, 0.03f), SnowShade, t);
            _snowColor.a = Mathf.Clamp(0.3f + 0.27f * contrast, 0f, 0.85f);

            while (Live.Count > 0 && Finished(Live.Peek())) Live.Dequeue();
            int over = Live.Count - Mathf.Max(1, max);
            for (; over > 0; over--)
            {
                Entry e = Live.Dequeue();
                if (Finished(e)) continue;
                FadeOut(e.Effect, TrimFadeSeconds);
                if (!_loggedCap)
                {
                    _loggedCap = true;
                    Plugin.Log.LogInfo($"Footprints: {max} snow prints reached; the oldest fade out.");
                }
            }

            // Pooled effects the game destroys leave their ids behind; forget them once nothing is tracked.
            if (Live.Count == 0 && Generations.Count > 2048) Generations.Clear();
        }

        private static bool Finished(Entry e)
        {
            if (e.Effect == null) return true;
            if (!Generations.TryGetValue(e.Id, out int generation) || generation != e.Generation) return true;   // reused
            return !e.Effect.gameObject.activeInHierarchy || !e.Effect.Particles.IsAlive(true);
        }

        /// <summary>Fades a print out over <paramref name="seconds"/>, from wherever it is in its own fade.</summary>
        private static void FadeOut(PoolableParticleSystem effect, float seconds)
        {
            GetPrints(effect);
            float life = seconds / (1f - Hold);
            foreach (ParticleSystem ps in Children)
            {
                int n = ps.GetParticles(Buffer);
                for (int i = 0; i < n; i++)
                {
                    ParticleSystem.Particle p = Buffer[i];
                    if (p.startLifetime <= 0f) continue;
                    float visible = Mathf.Min(1f, p.remainingLifetime / p.startLifetime / (1f - Hold));
                    if (p.remainingLifetime <= seconds * visible) continue;   // already going sooner
                    p.startLifetime = life;
                    p.remainingLifetime = seconds * visible;
                    Buffer[i] = p;
                }
                ps.SetParticles(Buffer, n);
            }
        }

        /// <summary>Hole changed: the previous hole's prints go at once.</summary>
        internal void Clear()
        {
            foreach (Entry e in Live)
                if (!Finished(e)) e.Effect.Stop(ParticleSystemStopBehavior.StopEmittingAndClear);
            Live.Clear();
            _loggedFirst = false;
            _loggedCap = false;
        }

        /// <summary>Off: snow prints fade out over a second, and new prints are the game's.</summary>
        public void Restore()
        {
            if (!_active && Live.Count == 0) return;
            _active = false;
            foreach (Entry e in Live)
                if (!Finished(e)) FadeOut(e.Effect, TrimFadeSeconds);
            Live.Clear();
        }
    }
}
