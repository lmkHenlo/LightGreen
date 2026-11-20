using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Cartoon rain on every course while Effects.CartoonRain is above 0, from the user's reference picture: easier to see
    /// than <see cref="Rain"/>, still read as rain.
    /// <list type="bullet">
    /// <item>Streaks: bold, nearly white, crisp-edged lines (a disc drawn in code by <see cref="RainArt"/>, stretched along
    /// the fall), slanted the same way: with the game's wind, and always at least a steady lean. Near, mid and far layers
    /// from the particle layer, about 2,700 at strength 1 (the rain has 3,800, fainter and thinner).</item>
    /// <item>Ripples: two white rings laid flat where drops land within 15 m of the camera, growing and fading over about
    /// 0.6 s, placed by a few ground checks a frame, as the rain's splashes are.</item>
    /// </list>
    /// Its sky, a soft blue mist that keeps the colours bright, is set in <see cref="Rain"/> with the others'. At night the
    /// streaks and ripples dim. Only one weather at a time (<see cref="Weather"/>).
    /// </summary>
    internal sealed class CartoonRain : IFeature
    {
        private readonly EnvironmentController _environment;
        private readonly ParticleLayer _layer;
        private readonly Texture2D _disc;
        private readonly Texture2D _ripple;

        private static readonly Color StreakColor = new Color(0.92f, 0.96f, 1f);

        /// <summary>Drift with the wind (m/s at calm and per km/h), and the least lean, so the streaks always slant.</summary>
        private const float DriftAtCalm = 0.3f;
        private const float DriftPerKmh = 0.12f;
        private const float LeastLean = 2.5f;
        private static readonly Vector2 CalmLean = new Vector2(0.6f, 0.8f);

        /// <summary>Ripples per second round the camera at strength 1, how far out, and how long one lasts.</summary>
        private const float RipplesPerSecond = 400f;
        private const float RippleRadius = 15f;

        private ParticleSystem _ripples;
        private float _rippleDebt;
        private Vector2 _wind;

        internal CartoonRain(EnvironmentController environment)
        {
            _environment = environment;
            _disc = Picture("LightGreen cartoon rain: streak", RainArt.Disc(32), 32);
            _ripple = Picture("LightGreen cartoon rain: ripple", RainArt.Ripple(128), 128);
            _layer = new ParticleLayer("LightGreen cartoon rain",
                new ParticleLayer.Spec
                {
                    Name = "near", Width = 30f, Height = 16f, Lift = 4f, Density = 0.08f, Fall = 10f,
                    MinSize = 0.025f, MaxSize = 0.035f, MaxScreenSize = 0.006f, Alpha = 0.85f, Stretch = 0.09f, Texture = _disc,
                },
                new ParticleLayer.Spec
                {
                    Name = "mid", Width = 90f, Height = 40f, Lift = 10f, Density = 0.003f, Fall = 11f,
                    MinSize = 0.06f, MaxSize = 0.08f, MaxScreenSize = 0.006f, Alpha = 0.7f, Stretch = 0.1f, Texture = _disc,
                },
                new ParticleLayer.Spec
                {
                    Name = "far", Width = 240f, Height = 70f, Lift = 25f, Density = 0.00015f, Fall = 12f,
                    MinSize = 0.22f, MaxSize = 0.28f, MaxScreenSize = 0.008f, Alpha = 0.35f, Stretch = 0.12f, Texture = _disc,
                });
        }

        public string Name => "Cartoon rain";

        public void Apply()
        {
            if (!GameEvents.InHole || Weather.Current != WeatherKind.CartoonRain)
            {
                Restore();
                return;
            }

            float strength = Weather.Strength;
            Vector2 direction = GameWind.Direction.sqrMagnitude > 1e-4f ? GameWind.Direction.normalized : CalmLean;
            Vector2 target = direction * Mathf.Max(LeastLean, DriftAtCalm + DriftPerKmh * GameWind.SpeedKmh);
            _wind = Vector2.MoveTowards(_wind, target, Time.deltaTime * 0.5f);

            float dark = Mathf.Clamp01(_environment.Darkness);
            Color color = StreakColor * Mathf.Lerp(1f, 0.4f, dark);
            color.a = 1f;

            Camera camera = GameManager.Camera;
            bool wasRunning = _layer.Running;
            _layer.Update(camera, strength, color, _wind);
            if (!wasRunning && _layer.Running) Plugin.LogChange("hole:cartoonrain", $"Cartoon rain: on, strength {strength:0.##}.");
            Ripple(camera, strength, color);
        }

        // ---- Ripples ------------------------------------------------------------------------------------------

        private void Ripple(Camera camera, float strength, Color color)
        {
            if (camera == null || !EnsureRipples()) return;
            _rippleDebt += RipplesPerSecond * strength * Time.deltaTime;
            int count = Mathf.Min(Mathf.FloorToInt(_rippleDebt), 30);
            _rippleDebt -= Mathf.FloorToInt(_rippleDebt);

            Vector3 eye = camera.transform.position;
            var emit = new ParticleSystem.EmitParams { startColor = new Color(color.r, color.g, color.b, 0.85f) };
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * RippleRadius;
                var origin = new Vector3(eye.x + offset.x, eye.y + 20f, eye.z + offset.y);
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                emit.position = hit.point + hit.normal * 0.03f;
                emit.rotation3D = new Vector3(90f, Random.Range(0f, 360f), 0f);   // flat on the ground
                emit.startSize = Random.Range(0.3f, 0.45f);
                emit.startLifetime = Random.Range(0.5f, 0.7f);
                _ripples.Emit(emit, 1);
            }
            if (!_ripples.gameObject.activeSelf) _ripples.gameObject.SetActive(true);
        }

        private bool EnsureRipples()
        {
            if (_ripples != null) return true;
            Material source = ParticleLayer.SharedMaterial();
            if (source == null) return false;

            var go = new GameObject("LightGreen cartoon rain: ripples") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _ripples = go.AddComponent<ParticleSystem>();
            _ripples.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ripples.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = true;
            main.gravityModifier = 0f;
            main.startSpeed = 0f;
            main.maxParticles = 1000;
            var emission = _ripples.emission;
            emission.enabled = false;
            var shape = _ripples.shape;
            shape.enabled = false;

            // Each ring grows from a quarter of its size and fades out.
            var size = _ripples.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.25f, 1f, 1f));
            var fade = _ripples.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.maxParticleSize = 0.5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(source) { name = "LightGreen cartoon rain: ripples", hideFlags = HideFlags.DontSave };
            material.SetTexture("_BaseMap", _ripple);
            renderer.sharedMaterial = material;
            _ripples.Play();
            return true;
        }

        private void StopRipples()
        {
            if (_ripples == null || (_ripples.particleCount == 0 && _rippleDebt == 0f)) return;
            _ripples.Clear();
            _rippleDebt = 0f;
        }

        // ---- Restore -------------------------------------------------------------------------------------------

        public void Restore()
        {
            _layer.Stop();
            StopRipples();
        }

        internal void Destroy()
        {
            Restore();
            _layer.Destroy();
            if (_ripples != null)
            {
                Object.Destroy(_ripples.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                Object.Destroy(_ripples.gameObject);
            }
            if (_disc != null) Object.Destroy(_disc);
            if (_ripple != null) Object.Destroy(_ripple);
        }

        private static Texture2D Picture(string name, float[] rgba, int size)
        {
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(Byte(rgba[i * 4]), Byte(rgba[i * 4 + 1]), Byte(rgba[i * 4 + 2]), Byte(rgba[i * 4 + 3]));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
