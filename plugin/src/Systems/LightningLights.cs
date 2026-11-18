using System.Collections.Generic;
using HarmonyLib;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Light from the Thunderstorm item's lightning. The game has no lightning weather: the item puts a small
    /// storm cloud over its target that strikes several times. Its strike effects don't go through the method
    /// <see cref="EffectLights"/> watches, so this has its own postfix, on the strike's effect method, which runs
    /// on every client for every player's storm.
    ///
    /// Each strike asks the light pool for two lights:
    /// <list type="bullet">
    /// <item>a bright, cold light where the bolt lands, with a shadow;</item>
    /// <item>a wide light just under the cloud, which lights the ground for a long way round from above. Point
    /// light falls off with distance, so the flash is strong under the storm and fades with distance from it,
    /// and against the sun by day it reads as slight while at night it lights the whole area.</item>
    /// </list>
    /// Both flicker two or three times over about half a second. When the player has turned off the game's
    /// "Explosion flashes" option, or in VR, it is one gentle pulse at half brightness instead (VR also halves
    /// the strength).
    /// </summary>
    internal sealed class LightningLights : IFeature
    {
        private readonly LightPool _pool;

        internal LightningLights(LightPool pool) => _pool = pool;

        public string Name => "Lightning";

        public void Apply()
        {
            float strength = Settings.Lightning.Value;
            if (!GameEvents.InHole || strength <= 0f)
            {
                Restore();
                return;
            }
            Update(_pool, strength, Vr.IsRunning);
        }

        private static readonly Color Bolt = new Color(0.78f, 0.86f, 1f);
        private static readonly Color Sky = new Color(0.72f, 0.8f, 1f);

        /// <summary>The light at the strike: intensity and range at strength 1.</summary>
        private const float StrikeIntensity = 24f;
        private const float StrikeRange = 18f;

        /// <summary>
        /// The light under the cloud. Point light falls off with the square of distance: 250 lights the ground
        /// 15 m below at about 1.1 (around a midday sun's worth), 50 m away at about 0.1, and is gone by 120 m.
        /// </summary>
        private const float SkyIntensity = 250f;
        private const float SkyRange = 120f;

        /// <summary>How high the cloud is when the strike's storm can't be found (the game aims for about this).</summary>
        private const float FallbackCloudHeight = 15f;

        /// <summary>A strike's flicker: a few pulses, each up at once and down over a few hundredths of a second.</summary>
        private const float FlickerSeconds = 0.5f;
        private const float PulseDecay = 0.04f;

        /// <summary>The gentle pulse: rises over 0.1 s and falls over 0.45 s, at half brightness.</summary>
        private const float GentleRise = 0.1f;
        private const float GentleSeconds = 0.55f;
        private const float GentlePeak = 0.5f;

        private sealed class Strike
        {
            internal Vector3 Ground;
            internal Vector3 Cloud;
            internal float Start;
            internal float[] PulseTimes;
            internal float[] PulseLevels;
        }

        private static readonly List<Strike> Incoming = new List<Strike>();
        private static bool _listening;

        private readonly List<Strike> _active = new List<Strike>();

        /// <summary>The postfix on a lightning strike's effects (sound, camera shake, bolt effect).</summary>
        [HarmonyPatch(typeof(ThunderstormLightningStrike), "PlayLightningStrikeEffectsInternal")]
        internal static class StrikePatch
        {
            private static void Postfix(ThunderstormLightningStrike __instance)
            {
                try
                {
                    if (!_listening || __instance == null) return;
                    Vector3 ground = __instance.transform.position;
                    Thunderstorm storm = __instance.NetworkowningThunderstorm;
                    Vector3 cloud = storm != null && storm.transform.position.y > ground.y
                        ? new Vector3(ground.x, storm.transform.position.y, ground.z)
                        : ground + Vector3.up * FallbackCloudHeight;
                    Incoming.Add(NewStrike(ground, cloud));
                }
                catch
                {
                    // Never let a light break the game's effect.
                }
            }
        }

        private static Strike NewStrike(Vector3 ground, Vector3 cloud)
        {
            // Two or three pulses, the first the brightest, at slightly different times for each strike.
            int count = Random.value < 0.5f ? 2 : 3;
            var times = new float[count];
            var levels = new float[count];
            times[0] = 0f;
            levels[0] = 1f;
            for (int i = 1; i < count; i++)
            {
                times[i] = times[i - 1] + Random.Range(0.07f, 0.14f);
                levels[i] = Random.Range(0.45f, 0.8f);
            }
            return new Strike { Ground = ground, Cloud = cloud, Start = Time.time, PulseTimes = times, PulseLevels = levels };
        }

        private void Update(LightPool pool, float strength, bool vrRunning)
        {
            _listening = true;

            _active.AddRange(Incoming);
            Incoming.Clear();
            if (_active.Count == 0) return;

            bool gentle = vrRunning || GameFlashes.Off;
            if (vrRunning) strength *= 0.5f;
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Strike s = _active[i];
                float age = now - s.Start;
                float level = gentle ? GentleLevel(age) : FlickerLevel(s, age);
                if (age >= (gentle ? GentleSeconds : FlickerSeconds)) { _active.RemoveAt(i); continue; }
                if (level <= 0.001f) continue;

                pool.Request(new LightRequest
                {
                    Position = s.Ground + Vector3.up,
                    Color = Bolt,
                    Intensity = StrikeIntensity * strength * level,
                    Range = StrikeRange,
                    Shadows = true,
                    Priority = 3.5f,   // above explosions: lightning gets a shadow first
                });
                pool.Request(new LightRequest
                {
                    Position = s.Cloud - Vector3.up * 2f,   // just under the cloud, so the cloud doesn't hide it
                    Color = Sky,
                    Intensity = SkyIntensity * strength * level,
                    Range = SkyRange,
                    Shadows = false,
                    Priority = 3.2f,
                });
            }
        }

        /// <summary>The sum of the strike's pulses, each up at once and dying away fast.</summary>
        private static float FlickerLevel(Strike s, float age)
        {
            float level = 0f;
            for (int p = 0; p < s.PulseTimes.Length; p++)
            {
                float t = age - s.PulseTimes[p];
                if (t < 0f) continue;
                level = Mathf.Max(level, s.PulseLevels[p] * Mathf.Exp(-t / PulseDecay));
            }
            // Fade the tail so the light is fully off when the strike ends.
            return level * Mathf.Clamp01((FlickerSeconds - age) / 0.1f);
        }

        /// <summary>One smooth pulse at half brightness, for players who turned flashes off and for VR.</summary>
        private static float GentleLevel(float age)
        {
            if (age < GentleRise) return GentlePeak * Mathf.SmoothStep(0f, 1f, age / GentleRise);
            float fall = Mathf.Clamp01(1f - (age - GentleRise) / (GentleSeconds - GentleRise));
            return GentlePeak * fall * fall;
        }

        public void Restore()
        {
            _listening = false;
            Incoming.Clear();
            _active.Clear();
        }
    }
}
