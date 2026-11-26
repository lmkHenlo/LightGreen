using System;
using System.Collections.Generic;
using System.Linq;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// The City at night. The game has two night City holes of its own (Causeway, Sky Lane): they use
    /// copies of the building materials with the windows switched on (the same window texture, with the
    /// emissive keyword on) and lampposts that carry spot lights. On the other City holes, made dark by
    /// LightGreen's time of day, the windows stay dark and the lampposts unlit. This does what the game's
    /// own night holes do, scaled by how dark it is:
    /// <list type="bullet">
    /// <item>Buildings' windows and vehicles' lights glow (not bus stops): materials that already
    /// have an emissive texture get the emissive keyword, as the game's night copies do. Only combinations
    /// the toon shader was built with, so nothing falls back to a variant missing a feature. Traffic lights
    /// are left alone (switching them on would light red, amber and green at once).</item>
    /// <item>Lampposts without lights of their own get a spot light from the light pool just under the lamp
    /// head, pointing down as the game's own night lampposts' do, so the head itself isn't lit. The nearest
    /// cast shadows when the pool has a shadow to spare.</item>
    /// </list>
    /// Bloom for the glow is the volume layer's (it lowers the bloom threshold on City holes at night).
    /// </summary>
    internal sealed class CityNight : IFeature
    {
        private readonly LightPool _pool;
        private readonly EnvironmentController _environment;

        internal CityNight(LightPool pool, EnvironmentController environment)
        {
            _pool = pool;
            _environment = environment;
        }

        public string Name => "City night";

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            Update(_pool, _environment, Settings.CityNight.Value);
        }

        private const string Emissive = "_EMISSIVEENABLE_ON";
        private const string EmissiveToggle = "_EmissiveEnable";
        private const string EmissiveColor = "_EmissiveColor";
        private const string EmissiveTex = "_EmissiveTex";

        /// <summary>Materials that glow at night, by name: buildings (windows) and vehicles.</summary>
        private static readonly string[] GlowPrefixes = { "M_Buildings_", "Bus ", "SmallCar", "Van", "Police Car" };

        /// <summary>Never glow: bus stops (their whole roof and sign light up, which the game's night holes don't do).</summary>
        private static readonly string[] NoGlow = { "Bus Station" };

        /// <summary>Emissive colour at full night, times the material's own (the game's night copies use 1).
        /// A little over 1 so the windows reach the bloom.</summary>
        private const float Glow = 1.5f;

        /// <summary>Lamppost light: the game's own night lampposts' colour and cone; brightness on the road
        /// below. The first try (1.6, about the game's own) was far too bright as a point light at the head;
        /// halved, and a spot now.</summary>
        private static readonly Color LampColor = new Color(1f, 0.95f, 0.85f);
        private const float LampGroundBrightness = 0.8f;
        private const float LampSpotAngle = 117f;

        private struct Lamp
        {
            internal Vector3 Position;
            internal float Intensity;
            internal float Range;
        }

        private readonly MaterialEdits _edits = new MaterialEdits();
        private readonly List<Material> _glowing = new List<Material>();
        private readonly HashSet<Material> _checked = new HashSet<Material>();
        private readonly List<Lamp> _lamps = new List<Lamp>();
        private readonly List<Material> _shared = new List<Material>();
        private readonly ObjectSweep<Renderer> _sweep = new ObjectSweep<Renderer>();
        private readonly ObjectSweep<Transform> _lampSweep = new ObjectSweep<Transform>();
        private readonly List<Lamp> _foundLamps = new List<Lamp>();
        private bool _sweepFirst;
        private int _added;
        private float _nextScan;
        private bool _scanned;
        private float _appliedLevel = -1f;
        private bool _logged;
        private int _skipped;

        /// <summary>Called on a new hole: look for lampposts and materials again.</summary>
        internal void Rescan()
        {
            _lampSweep.Stop();
            _scanned = false;
            _logged = false;
            _skipped = 0;
        }

        private void Update(LightPool pool, EnvironmentController environment, float strength)
        {
            bool city = GameEvents.CurrentBiome == Biome.City;
            float level = city ? Mathf.Clamp01(environment.Darkness) * strength : 0f;
            if (level <= 0.01f)
            {
                if (_glowing.Count > 0 || _scanned) Restore();
                return;
            }

            if (!_scanned || _sweep.Running || Time.unscaledTime >= _nextScan) Scan();
            if (_lampSweep.Running) FindLamps();
            ApplyGlow(level);

            foreach (Lamp lamp in _lamps)
            {
                pool.Request(new LightRequest
                {
                    Position = lamp.Position,
                    Color = LampColor,
                    Intensity = lamp.Intensity * level,
                    Range = lamp.Range,
                    Shadows = true,
                    Priority = 1.5f,   // below explosions and lightning, above other balls' glow
                    SpotAngle = LampSpotAngle,
                });
            }
        }

        /// <summary>
        /// Looks through the renderers in the world for materials that should glow, a few frames at a time
        /// (<see cref="ObjectSweep{T}"/>); what's found glows when the sweep ends.
        /// </summary>
        private void Scan()
        {
            if (!_sweep.Running)
            {
                _sweepFirst = !_scanned;
                _scanned = true;
                _nextScan = Time.unscaledTime + 15f;   // vehicles and props can appear after the hole loads
                _edits.ForgetDestroyed();
                _glowing.RemoveAll(m => m == null);
                _added = 0;
                _sweep.Start(FindObjectsInactive.Exclude);
            }

            _sweep.BeginFrame();
            while (_sweep.Next(out Renderer r))
            {
                r.GetSharedMaterials(_shared);
                foreach (Material m in _shared)
                {
                    if (m == null || !_checked.Add(m)) continue;
                    if (!ShouldGlow(m)) continue;
                    _glowing.Add(m);
                    _added++;
                }
            }
            _shared.Clear();
            if (!_sweep.EndFrame()) return;
            if (_added > 0) _appliedLevel = -1f;

            // The lampposts are looked for once a hole, after the first look for materials, also a few frames at a time.
            if (_sweepFirst)
            {
                _foundLamps.Clear();
                _lampSweep.Start(FindObjectsInactive.Exclude);
            }
        }

        private bool ShouldGlow(Material m)
        {
            string name = m.name.Replace(" (Instance)", "");
            if (!StartsWithAny(name, GlowPrefixes) || StartsWithAny(name, NoGlow)) return false;
            if (!m.HasProperty(EmissiveTex) || m.GetTexture(EmissiveTex) == null || !m.HasProperty(EmissiveColor)) return false;
            if (m.IsKeywordEnabled(Emissive)) return false;   // already glowing: the game's own night copies
            if (!ToonEmissive.HasVariant(m)) { _skipped++; return false; }
            return true;
        }

        private void ApplyGlow(float level)
        {
            if (Mathf.Abs(level - _appliedLevel) < 0.01f) return;
            _appliedLevel = level;
            foreach (Material m in _glowing)
            {
                if (m == null) continue;
                _edits.SetKeyword(m, Emissive, true);
                if (m.HasProperty(EmissiveToggle)) _edits.SetFloat(m, EmissiveToggle, 1f);
                Color stock = _edits.StockColor(m, EmissiveColor);
                float k = Glow * level;
                _edits.SetColor(m, EmissiveColor, new Color(stock.r * k, stock.g * k, stock.b * k, stock.a));
            }
        }

        /// <summary>
        /// Lampposts (objects named "Lamppost…" or "Lampost…") that don't carry a light of their own. The light
        /// goes just under the lamp head: the post stands at the object's origin and the arm reaches out to the
        /// head, so the head is about twice as far out as the middle of the whole lamppost (for a post with an
        /// arm each side the middle is the post, and the light goes on top of it). Brightness follows height.
        /// </summary>
        private void FindLamps()
        {
            _lampSweep.BeginFrame();
            while (_lampSweep.Next(out Transform t))
            {
                if (!IsLamppost(t) || (t.parent != null && IsLamppost(t.parent))) continue;
                if (t.GetComponentInChildren<Light>(true) != null) continue;   // the game's own lit lampposts

                Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float height = b.size.y;
                if (height < 2f || height > 30f) continue;

                Vector3 post = t.position;
                Vector3 reach = new Vector3(b.center.x - post.x, 0f, b.center.z - post.z) * 2f;
                if (reach.magnitude > 5f) reach = Vector3.zero;   // the origin isn't at the post: stay central
                Vector3 head = new Vector3(post.x, b.max.y - 0.8f, post.z) + reach;
                if (reach == Vector3.zero) head = new Vector3(b.center.x, b.max.y - 0.8f, b.center.z);
                float drop = Mathf.Max(2f, head.y - b.min.y);

                _foundLamps.Add(new Lamp
                {
                    Position = head,
                    Intensity = Mathf.Clamp(LampGroundBrightness * drop * drop, 5f, 200f),
                    Range = Mathf.Clamp(drop * 1.8f, 8f, 30f),
                });
            }
            if (!_lampSweep.EndFrame()) return;

            // Swapped in whole, so the lamps of the last look stay lit until this one is done.
            _lamps.Clear();
            _lamps.AddRange(_foundLamps);
            _foundLamps.Clear();
            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo($"City night: {_glowing.Count} materials glow ({string.Join(", ", _glowing.Take(8).Select(m => m.name))}" +
                                   $"{(_glowing.Count > 8 ? ", …" : "")}); {_lamps.Count} lampposts lit" +
                                   (_skipped > 0 ? $"; {_skipped} left dark (no glowing variant in the game's toon shader)." : ".") +
                                   $" Looked through {_sweep.Count} renderers and {_lampSweep.Count} objects in {_sweep.Frames + _lampSweep.Frames} frames.");
            }
        }

        private static bool IsLamppost(Transform t)
        {
            string name = t.name;
            return name.StartsWith("Lamppost", StringComparison.Ordinal) || name.StartsWith("Lampost", StringComparison.Ordinal);
        }

        private static bool StartsWithAny(string name, string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        public void Restore()
        {
            _sweep.Stop();
            _lampSweep.Stop();
            _foundLamps.Clear();
            if (_glowing.Count == 0 && !_scanned && _checked.Count == 0) return;
            _edits.Restore();
            _glowing.Clear();
            _checked.Clear();
            _lamps.Clear();
            _scanned = false;
            _skipped = 0;
            _appliedLevel = -1f;
        }
    }
}
