using System;
using System.Collections.Generic;
using HarmonyLib;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Light from the game's effects: explosions (mine, rocket, orbital laser) light the ground and
    /// players around them for under a second and the big ones cast shadows; muzzle flashes pop; rocket
    /// and freeze-bomb trails carry a light with them. Every effect the game plays goes through one
    /// method, for the local player and for effects other players cause alike, so a postfix there sees
    /// them all. Each effect type's light comes from the table below; lights come from the light pool.
    /// When the player has turned off the game's "Explosion flashes" option, flashes rise more slowly and
    /// reach half the brightness.
    /// </summary>
    internal sealed class EffectLights : IFeature
    {
        private readonly LightPool _pool;

        internal EffectLights(LightPool pool) => _pool = pool;

        public string Name => "Effect lights";

        public void Apply()
        {
            float strength = Settings.EffectLights.Value;
            if (!GameEvents.InHole || strength <= 0f)
            {
                Restore();
                return;
            }
            Update(_pool, strength);
        }

        /// <summary>One effect type's light.</summary>
        private sealed class EffectLook
        {
            internal Color Color;
            internal float Intensity;   // at strength 1
            internal float Range;       // metres
            internal float Seconds;     // how long a flash lasts; for a trail, how long it fades after the effect stops
            internal bool Shadows;
            internal bool Follows;      // a trail: the light rides with the effect while it plays
            internal float Priority;
        }

        private static readonly Color Fire = new Color(1f, 0.58f, 0.28f);
        private static readonly Color Flash = new Color(1f, 0.82f, 0.55f);
        private static readonly Color Laser = new Color(0.62f, 0.8f, 1f);
        private static readonly Color Ice = new Color(0.55f, 0.85f, 1f);

        private static readonly Dictionary<VfxType, EffectLook> Looks = new Dictionary<VfxType, EffectLook>
        {
            // Explosions: the largest, with shadows.
            [VfxType.MineExplosion] = new EffectLook { Color = Fire, Intensity = 14f, Range = 14f, Seconds = 0.45f, Shadows = true, Priority = 3f },
            [VfxType.RocketLauncherRocketExplosion] = new EffectLook { Color = Fire, Intensity = 14f, Range = 15f, Seconds = 0.45f, Shadows = true, Priority = 3f },
            [VfxType.OrbitalLaserExplosion] = new EffectLook { Color = Laser, Intensity = 18f, Range = 20f, Seconds = 0.7f, Shadows = true, Priority = 3f },
            // Shots and blasts: brief, no shadows.
            [VfxType.RocketLauncherMuzzle] = new EffectLook { Color = Flash, Intensity = 6f, Range = 6f, Seconds = 0.12f, Priority = 2f },
            [VfxType.RocketLauncherBackBlast] = new EffectLook { Color = Flash, Intensity = 5f, Range = 6f, Seconds = 0.15f, Priority = 2f },
            [VfxType.ShotgunMuzzle] = new EffectLook { Color = Flash, Intensity = 6f, Range = 5f, Seconds = 0.08f, Priority = 2f },
            [VfxType.DuelingPistolMuzzle] = new EffectLook { Color = Flash, Intensity = 5f, Range = 4f, Seconds = 0.08f, Priority = 2f },
            [VfxType.FreezeBombMuzzle] = new EffectLook { Color = Ice, Intensity = 4f, Range = 4f, Seconds = 0.12f, Priority = 2f },
            [VfxType.FreezeBombImpact] = new EffectLook { Color = Ice, Intensity = 8f, Range = 8f, Seconds = 0.35f, Priority = 2f },
            [VfxType.RailgunShoot] = new EffectLook { Color = Laser, Intensity = 8f, Range = 8f, Seconds = 0.15f, Priority = 2f },
            // Trails: a moving light while the trail plays.
            [VfxType.RocketLauncherRocketTrail] = new EffectLook { Color = Fire, Intensity = 4f, Range = 6f, Seconds = 0.25f, Follows = true, Priority = 1f },
            [VfxType.RocketDriverTrail] = new EffectLook { Color = Fire, Intensity = 2.5f, Range = 4f, Seconds = 0.25f, Follows = true, Priority = 1f },
            [VfxType.FreezeBombTrail] = new EffectLook { Color = Ice, Intensity = 2.5f, Range = 4f, Seconds = 0.25f, Follows = true, Priority = 1f },
        };

        /// <summary>A light being shown: where, which look, since when.</summary>
        private sealed class Active
        {
            internal EffectLook Look;
            internal Vector3 Position;
            internal Transform Follow;
            internal PoolableParticleSystem Effect;
            internal float Start;
            internal float Stopped = -1f;   // trails: when the effect stopped playing
        }

        /// <summary>Effects seen by the postfix since the last frame (only while the effect is on).</summary>
        private static readonly List<Active> Incoming = new List<Active>();
        private static bool _listening;

        private readonly List<Active> _active = new List<Active>();

        /// <summary>The postfix on the game's one effect-playing method.</summary>
        [HarmonyPatch(typeof(VfxManager), "PlayPooledVfxLocalOnlyInternal",
            new[] { typeof(VfxType), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Transform), typeof(bool), typeof(float), typeof(Action<PoolableParticleSystem>) })]
        internal static class PlayedPatch
        {
            private static readonly PatchGuard Guard = new PatchGuard("Effect lights hook");

            private static void Postfix(VfxType vfxType, float delay, PoolableParticleSystem __result)
            {
                if (Guard.Broken) return;
                try
                {
                    if (!_listening || __result == null || !Looks.TryGetValue(vfxType, out EffectLook look)) return;
                    Incoming.Add(new Active
                    {
                        Look = look,
                        Position = __result.transform.position,
                        Follow = look.Follows ? __result.transform : null,
                        Effect = look.Follows ? __result : null,
                        Start = Time.time + Mathf.Max(0f, delay),
                    });
                }
                catch (Exception e)
                {
                    Guard.Fail(e);   // never let a light break the game's effect
                }
            }
        }

        private void Update(LightPool pool, float strength)
        {
            _listening = true;

            _active.AddRange(Incoming);
            Incoming.Clear();
            if (_active.Count == 0) return;
            bool gentle = GameFlashes.Off;
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Active a = _active[i];
                if (now < a.Start) continue;   // a delayed effect hasn't started yet
                float level;
                Vector3 position = a.Position;
                if (a.Look.Follows)
                {
                    bool playing = a.Effect != null && a.Effect.isActiveAndEnabled && a.Effect.IsPlaying;
                    if (playing && a.Follow != null) a.Position = position = a.Follow.position;
                    if (!playing && a.Stopped < 0f) a.Stopped = now;
                    // Full while the trail plays (after a short rise), then out over Seconds; never past 15 s.
                    level = Mathf.Clamp01((now - a.Start) / 0.05f);
                    if (a.Stopped >= 0f) level *= 1f - (now - a.Stopped) / a.Look.Seconds;
                    if (level <= 0f || now - a.Start > 15f) { _active.RemoveAt(i); continue; }
                }
                else
                {
                    // A flash: up in 30 ms, then down along a curve that drops fast and tails off. Gentle (the
                    // game's explosion flashes off): up over 0.1 s, half as bright, lasting half as long again.
                    float age = now - a.Start;
                    float seconds = gentle ? a.Look.Seconds * 1.5f : a.Look.Seconds;
                    if (age >= seconds) { _active.RemoveAt(i); continue; }
                    float fall = 1f - age / seconds;
                    level = gentle
                        ? 0.5f * Mathf.SmoothStep(0f, 1f, age / 0.1f) * fall * fall
                        : Mathf.Clamp01(age / 0.03f) * fall * fall;
                }

                pool.Request(new LightRequest
                {
                    Position = position + Vector3.up * 0.5f,   // just above the effect, so the ground it sits on is lit
                    Color = a.Look.Color,
                    Intensity = a.Look.Intensity * strength * level,
                    Range = a.Look.Range,
                    Shadows = a.Look.Shadows,
                    Priority = a.Look.Priority,
                });
            }
        }

        public void Restore()
        {
            _listening = false;
            Incoming.Clear();
            _active.Clear();
        }
    }
}
