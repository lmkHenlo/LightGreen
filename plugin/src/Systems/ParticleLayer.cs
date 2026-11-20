using System.Collections.Generic;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Weather particles around the camera: rain and snow. Each layer is a particle system
    /// built in code whose emitter follows the camera (the spectator and flyover cameras included); its
    /// particles live in world space, so they keep their place as the camera moves and drift with the wind.
    /// On a camera cut (a jump of more than 25 m in one frame) every layer is cleared and refilled at once,
    /// so the new view never starts empty or pops in.
    ///
    /// The particles are drawn with a copy of the game's own particle material ("ParticlesUnlit": URP's
    /// unlit particle shader with its soft round texture, the variant the game builds), so nothing is shipped
    /// for them and they take the game's fog. If the game's material can't be found, URP's shader is used with
    /// a soft dot drawn in code.
    /// </summary>
    internal sealed class ParticleLayer
    {
        /// <summary>One layer of particles at density 1.</summary>
        internal struct Spec
        {
            internal string Name;
            /// <summary>Width and depth of the box round the camera, its height, and how far its centre is above the camera.</summary>
            internal float Width, Height, Lift;
            /// <summary>Particles per cubic metre.</summary>
            internal float Density;
            /// <summary>Fall speed, m/s.</summary>
            internal float Fall;
            internal float MinSize, MaxSize;
            /// <summary>Largest a particle may get on screen, as a fraction of the screen: keeps particles that pass
            /// right by the lens from covering the ball.</summary>
            internal float MaxScreenSize;
            internal float Alpha;
            /// <summary>Streaks instead of dots (rain): each particle is drawn this many seconds of its motion long. 0 for dots.</summary>
            internal float Stretch;
            /// <summary>The layer's own picture in place of the game's soft dot (snowflakes), or null; with Tiles above 1,
            /// an atlas of Tiles × Tiles pictures, each particle showing one at random.</summary>
            internal Texture2D Texture;
            internal int Tiles;
            /// <summary>Wander as they fall (snow), in m/s; 0 for none.</summary>
            internal float Sway;
            /// <summary>Turning as they fall, up to this many radians a second either way; 0 for none.</summary>
            internal float Spin;
        }

        private const string ShaderName = "Universal Render Pipeline/Particles/Unlit";
        private const string GameMaterialName = "ParticlesUnlit";
        private const float CutDistance = 25f;

        private static Material _source;
        private static float _nextLook;
        private static bool _loggedSource;

        private readonly Spec[] _specs;
        private readonly string _rootName;
        private GameObject _root;
        private readonly List<ParticleSystem> _systems = new List<ParticleSystem>();
        private readonly List<Material> _materials = new List<Material>();
        private Vector3 _lastCamera;
        private bool _running;
        private float _density = -1f;

        internal ParticleLayer(string rootName, params Spec[] specs)
        {
            _rootName = rootName;
            _specs = specs;
        }

        /// <summary>
        /// Shows the layers round the camera this frame. density scales the particle count (1 is the specs');
        /// color tints them (alpha multiplies each layer's own); wind is the drift, world x and z, in m/s.
        /// </summary>
        internal bool Running => _running;

        internal void Update(Camera camera, float density, Color color, Vector2 wind)
        {
            if (camera == null || density <= 0f)
            {
                Stop();
                return;
            }
            if (!EnsureBuilt()) return;

            Vector3 eye = camera.transform.position;
            bool cut = !_running || (eye - _lastCamera).sqrMagnitude > CutDistance * CutDistance;
            bool densityChanged = Mathf.Abs(density - _density) > 0.01f;
            _lastCamera = eye;
            if (!_root.activeSelf) _root.SetActive(true);   // before simulating: an inactive system doesn't

            for (int i = 0; i < _systems.Count; i++)
            {
                Spec spec = _specs[i];
                ParticleSystem ps = _systems[i];
                ps.transform.position = eye + Vector3.up * spec.Lift;

                if (densityChanged) SetDensity(ps, spec, density);

                var velocity = ps.velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(wind.x * 0.8f, wind.x * 1.2f);
                velocity.y = new ParticleSystem.MinMaxCurve(-spec.Fall * 1.25f, -spec.Fall * 0.75f);
                velocity.z = new ParticleSystem.MinMaxCurve(wind.y * 0.8f, wind.y * 1.2f);

                Color c = color;
                c.a *= spec.Alpha;
                _materials[i].SetColor("_BaseColor", c);

                if (cut)
                {
                    // Fill the new view at once: as if it had been falling for a whole lifetime already.
                    ps.Clear(true);
                    ps.Simulate(Lifetime(spec), true, true, false);
                    ps.Play(true);
                }
                else if (!ps.isPlaying)
                {
                    ps.Play(true);
                }
            }
            if (densityChanged) _density = density;
            _running = true;
        }

        private static float Lifetime(Spec spec) => spec.Height / Mathf.Max(0.1f, spec.Fall);

        private static void SetDensity(ParticleSystem ps, Spec spec, float density)
        {
            float lifetime = Lifetime(spec);
            float count = spec.Width * spec.Width * spec.Height * spec.Density * density;
            var main = ps.main;
            main.maxParticles = Mathf.CeilToInt(count * 1.3f) + 16;
            var emission = ps.emission;
            // Born anywhere in the box with up to a full fall's life: the box stays evenly filled.
            emission.rateOverTime = count / (lifetime * 0.75f);
        }

        private bool EnsureBuilt()
        {
            if (_root != null) return true;
            Material source = Source();
            if (source == null) return false;

            _root = new GameObject(_rootName) { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(_root);
            _root.SetActive(false);
            foreach (Spec spec in _specs) _systems.Add(Build(spec, source));
            return true;
        }

        private ParticleSystem Build(Spec spec, Material source)
        {
            var go = new GameObject($"{_rootName}: {spec.Name}") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(_root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float lifetime = Lifetime(spec);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.5f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.MinSize, spec.MaxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(spec.Width, spec.Height, spec.Width);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-spec.Fall, -spec.Fall);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            if (spec.Sway > 0f)
            {
                // Unity's own noise at its cheapest: a slow, gentle wander.
                var noise = ps.noise;
                noise.enabled = true;
                noise.separateAxes = false;
                noise.strength = spec.Sway;
                noise.frequency = 0.35f;
                noise.scrollSpeed = 0.15f;
                noise.damping = true;
                noise.octaveCount = 1;
                noise.quality = ParticleSystemNoiseQuality.Low;
            }
            if (spec.Spin > 0f)
            {
                var spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-spec.Spin, spec.Spin);
            }
            if (spec.Texture != null && spec.Tiles > 1)
            {
                // One picture of the atlas per particle, kept for its life.
                var sheet = ps.textureSheetAnimation;
                sheet.enabled = true;
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = spec.Tiles;
                sheet.numTilesY = spec.Tiles;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, spec.Tiles * spec.Tiles - 0.001f);
                sheet.cycleCount = 1;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (spec.Stretch > 0f)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = spec.Stretch;
                renderer.lengthScale = 1f;
                renderer.cameraVelocityScale = 0f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.maxParticleSize = spec.MaxScreenSize;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(source) { name = $"{_rootName}: {spec.Name}", hideFlags = HideFlags.DontSave };
            if (spec.Texture != null) material.SetTexture("_BaseMap", spec.Texture);
            renderer.sharedMaterial = material;
            _materials.Add(material);
            return ps;
        }

        /// <summary>Stops and clears every layer (they're rebuilt into the view when shown again).</summary>
        internal void Stop()
        {
            if (!_running) return;
            foreach (ParticleSystem ps in _systems)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_root != null) _root.SetActive(false);
            _running = false;
        }

        internal void Destroy()
        {
            Stop();
            if (_root != null) Object.Destroy(_root);
            foreach (Material m in _materials) if (m != null) Object.Destroy(m);
            _root = null;
            _systems.Clear();
            _materials.Clear();
            _density = -1f;
        }

        // ---- The material --------------------------------------------------------------------------

        /// <summary>
        /// The particle material every weather layer is copied from, or null until it's found: a copy of the game's
        /// own particle material, made the first time it's seen loaded and kept, so it stays usable after the scene
        /// that loaded it unloads. Looked for every 2 s until found.
        /// </summary>
        internal static Material SharedMaterial() => Source();

        private static Material Source()
        {
            if (_source != null) return _source;
            if (Time.unscaledTime < _nextLook) return null;
            _nextLook = Time.unscaledTime + 2f;

            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.name != GameMaterialName || m.shader == null || m.shader.name != ShaderName) continue;
                _source = new Material(m) { name = "LightGreen particles", hideFlags = HideFlags.HideAndDontSave };
                Log("the game's own particle material");
                return _source;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return null;
            _source = new Material(shader) { name = "LightGreen particles", hideFlags = HideFlags.HideAndDontSave };
            _source.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _source.SetFloat("_Surface", 1f);
            _source.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _source.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _source.SetFloat("_ZWrite", 0f);
            _source.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _source.SetTexture("_BaseMap", SoftDot());
            Log("URP's particle shader with a soft dot (the game's particle material wasn't found)");
            return _source;
        }

        private static void Log(string what)
        {
            if (_loggedSource) return;
            _loggedSource = true;
            Plugin.Log.LogInfo($"Weather particles: drawn with {what}.");
        }

        /// <summary>A round, soft-edged white dot, for when the game's particle texture isn't available.</summary>
        private static Texture2D SoftDot()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "LightGreen soft dot", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
