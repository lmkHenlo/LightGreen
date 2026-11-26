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
    /// <para>
    /// The game's values are kept as copies LightGreen owns (<see cref="ParticleValues"/>), and are only put back on a
    /// print LightGreen changed. Unity's getters for a module's curves and gradients hand out wrappers around that
    /// particle system's own native data; 0.1.0 to 0.3.0 kept the first print's, and once that print was destroyed every
    /// sand print copied a freed gradient back into the game's prints. A gradient with garbage key counts overran
    /// the stack in Unity's particle render job and crashed the game (a memory dump, 2026-11-21: Job.Worker,
    /// "Footprint Particle", 3 of 12 prints with corrupt gradients).
    /// </para>
    /// </summary>
    internal sealed class Footprints : IFeature, IMapAware
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
        private bool _hooked;        // the callback is added once the effect has run at all
        private bool _active;
        private float _seconds = 45f;
        private Color _snowColor = SnowShade;
        private bool _step;          // inside ShowFootprint, on snow, for a player you can see
        private static readonly Gradient HoldGradient = MakeHoldGradient();
        // The one Footprints (made by the plugin), for the patches and the game's effect callback, which are static.
        private static Footprints _instance;
        private static readonly Action<PoolableParticleSystem> Callback = effect => _instance?.Configure(effect);

        internal Footprints() => _instance = this;

        private List<Original> _originals;
        /// <summary>Print particle systems given the snow look, by instance id, to put back as the game had them.</summary>
        private readonly Dictionary<int, ParticleSystem> _changed = new Dictionary<int, ParticleSystem>();
        private readonly List<int> _gone = new List<int>();
        /// <summary>Size of <see cref="_changed"/> at which the ids of destroyed prints are dropped.</summary>
        private int _pruneAt = PruneFloor;
        private const int PruneFloor = 1024;
        private readonly List<ParticleSystem> _children = new List<ParticleSystem>();
        private readonly Queue<Entry> _live = new Queue<Entry>();
        private readonly Dictionary<int, int> _generations = new Dictionary<int, int>();
        // The particles of one print system while its fade is shortened; grows to the most a print has had, so every one
        // of them is faded, whatever the game's prints hold.
        private ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[8];
        private bool _loggedFirst;
        private bool _loggedCap;

        /// <summary>Notes whether this step is a snow step for a player the local player can see.</summary>
        [HarmonyPatch(typeof(PlayerMovementVfx), "ShowFootprint")]
        internal static class StepPatch
        {
            private static readonly PatchGuard Guard = new PatchGuard("Footsteps hook");

            private static void Prefix(PlayerVfx ___playerVfx)
            {
                Footprints prints = _instance;
                if (prints == null) return;
                prints._step = false;
                if (!prints._active || Guard.Broken) return;
                try
                {
                    PlayerInfo player = ___playerVfx != null ? ___playerVfx.PlayerInfo : null;
                    if (player == null || player.Movement == null) return;
                    PlayerInfo local = GameManager.LocalPlayerInfo;
                    if (local != null && local != player && !local.CanDetectSmokeBombInvisiblePlayer(player)) return;
                    prints._step = IsSnow(player.Movement.GroundTerrainDominantGlobalLayer);
                }
                catch (Exception e)
                {
                    prints._step = false;
                    Guard.Fail(e);
                }
            }

            private static Exception Finalizer(Exception __exception)
            {
                if (_instance != null) _instance._step = false;
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
                if (_instance == null || !_instance._hooked || vfxType != VfxType.CharacterFootprint) return;
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
        private static readonly PatchGuard EffectGuard = new PatchGuard("Footprint effects hook");

        private void Configure(PoolableParticleSystem effect)
        {
            if (EffectGuard.Broken) return;
            try
            {
                if (effect == null) return;
                bool snow = _active && _step;
                // Every play counts as a new use, so a tracked snow print that came back as a sand print is let go.
                int id = effect.GetInstanceID();
                _generations.TryGetValue(id, out int generation);
                _generations[id] = ++generation;
                GetPrints(effect);
                if (_originals == null) Capture();
                for (int i = 0; i < _children.Count && i < _originals.Count; i++)
                {
                    ParticleSystem ps = _children[i];
                    int psId = ps.GetInstanceID();
                    // A print LightGreen never changed keeps the game's values as they are: nothing to put back.
                    if (!snow && !_changed.Remove(psId)) continue;
                    Original o = _originals[i];
                    ParticleSystem.MainModule main = ps.main;
                    ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                    if (snow)
                    {
                        main.startLifetime = _seconds;
                        main.startColor = _snowColor;
                        fade.enabled = true;
                        fade.color = HoldGradient;
                        _changed[psId] = ps;
                    }
                    else
                    {
                        main.startLifetime = o.Lifetime;
                        main.startColor = o.StartColor;
                        fade.enabled = o.FadeEnabled;
                        fade.color = o.Fade;
                    }
                }
                // Prints the game destroyed leave their ids behind: drop those, and only those, so every print still
                // in the pool with the snow look is put back when it's used off snow.
                if (_changed.Count > _pruneAt) PruneChanged();
                if (snow) Track(effect, id, generation);
            }
            catch (Exception e)
            {
                EffectGuard.Fail(e);   // never let a footprint break the game's effect
            }
        }

        /// <summary>The effect's print particle systems (its children; the root one draws nothing).</summary>
        private void GetPrints(PoolableParticleSystem effect)
        {
            _children.Clear();
            effect.GetComponentsInChildren(true, _children);
            ParticleSystem root = effect.Particles;
            _children.RemoveAll(ps => ps == root);
        }

        /// <summary>
        /// The game's own values, from the first print seen (every print comes from one prefab), copied: the getters'
        /// curves and gradients belong to that print and go when it's destroyed.
        /// </summary>
        private void Capture()
        {
            _originals = new List<Original>();
            foreach (ParticleSystem ps in _children)
            {
                ParticleSystem.MainModule main = ps.main;
                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                _originals.Add(new Original
                {
                    Lifetime = ParticleValues.Copy(main.startLifetime),
                    StartColor = ParticleValues.Copy(main.startColor),
                    FadeEnabled = fade.enabled,
                    Fade = ParticleValues.Copy(fade.color),
                });
            }
        }

        private void Track(PoolableParticleSystem effect, int id, int generation)
        {
            _live.Enqueue(new Entry { Effect = effect, Id = id, Generation = generation });
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

            while (_live.Count > 0 && Finished(_live.Peek())) _live.Dequeue();
            int over = _live.Count - Mathf.Max(1, max);
            for (; over > 0; over--)
            {
                Entry e = _live.Dequeue();
                if (Finished(e)) continue;
                FadeOut(e.Effect, TrimFadeSeconds);
                if (!_loggedCap)
                {
                    _loggedCap = true;
                    Plugin.Log.LogInfo($"Footprints: {max} snow prints reached; the oldest fade out.");
                }
            }

            // Pooled effects the game destroys leave their ids behind; forget them once nothing is tracked.
            if (_live.Count == 0 && _generations.Count > 2048) _generations.Clear();
        }

        private void PruneChanged()
        {
            _gone.Clear();
            foreach (KeyValuePair<int, ParticleSystem> pair in _changed)
                if (pair.Value == null) _gone.Add(pair.Key);
            foreach (int id in _gone) _changed.Remove(id);
            _gone.Clear();
            // Next time at twice what's left, so a pool that really is this big isn't gone through on every step.
            _pruneAt = Mathf.Max(PruneFloor, _changed.Count * 2);
        }

        private bool Finished(Entry e)
        {
            if (e.Effect == null) return true;
            if (!_generations.TryGetValue(e.Id, out int generation) || generation != e.Generation) return true;   // reused
            return !e.Effect.gameObject.activeInHierarchy || !e.Effect.Particles.IsAlive(true);
        }

        /// <summary>Fades a print out over <paramref name="seconds"/>, from wherever it is in its own fade.</summary>
        private void FadeOut(PoolableParticleSystem effect, float seconds)
        {
            GetPrints(effect);
            float life = seconds / (1f - Hold);
            foreach (ParticleSystem ps in _children)
            {
                if (_buffer.Length < ps.particleCount) _buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(ps.particleCount)];
                ParticleSystem.Particle[] buffer = _buffer;
                int n = ps.GetParticles(buffer);
                for (int i = 0; i < n; i++)
                {
                    ParticleSystem.Particle p = buffer[i];
                    if (p.startLifetime <= 0f) continue;
                    float visible = Mathf.Min(1f, p.remainingLifetime / p.startLifetime / (1f - Hold));
                    if (p.remainingLifetime <= seconds * visible) continue;   // already going sooner
                    p.startLifetime = life;
                    p.remainingLifetime = seconds * visible;
                    buffer[i] = p;
                }
                ps.SetParticles(buffer, n);
            }
        }

        public void OnBiomeChanged()
        {
        }

        public void OnHoleChanged() => Clear();

        /// <summary>Hole changed: the previous hole's prints go at once.</summary>
        internal void Clear()
        {
            foreach (Entry e in _live)
                if (!Finished(e)) e.Effect.Stop(ParticleSystemStopBehavior.StopEmittingAndClear);
            _live.Clear();
            _loggedFirst = false;
            _loggedCap = false;
        }

        /// <summary>Off: snow prints fade out over a second, and new prints are the game's.</summary>
        public void Restore()
        {
            if (!_active && _live.Count == 0) return;
            _active = false;
            foreach (Entry e in _live)
                if (!Finished(e)) FadeOut(e.Effect, TrimFadeSeconds);
            _live.Clear();
        }
    }
}
