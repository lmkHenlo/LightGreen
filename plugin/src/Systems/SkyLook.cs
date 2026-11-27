using LightGreen.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightGreen.Systems
{
    /// <summary>
    /// The sky and the ambient (fill) light for <see cref="EnvironmentController"/>: the sky material recoloured for the
    /// time of day and the weather, and the hole's own ambient shifted by how much the sky's brightness changed. Both
    /// are captured with the scene and put back exactly on <see cref="Restore"/>.
    /// </summary>
    internal sealed class SkyLook
    {
        /// <summary>Under a full overcast, how far the sky's colours go to grey.</summary>
        private const float OvercastSkyGrey = 0.7f;

        /// <summary>Under a full snow sky: its bright, pale blue colours (top, horizon, the glow round the sun), and how far
        /// the sky goes to them.</summary>
        private static readonly Color SnowSkyTop = new Color(0.62f, 0.76f, 0.97f);
        private static readonly Color SnowSkyHorizon = new Color(0.9f, 0.95f, 1f);
        private static readonly Color SnowSkyGlow = new Color(0.95f, 0.97f, 1f);
        private const float SnowSkyBlend = 0.9f;

        /// <summary>Under full cartoon-rain mist: the sky's misty blues and how far it goes to them.</summary>
        private static readonly Color MistSkyTop = new Color(0.55f, 0.72f, 0.86f);
        private static readonly Color MistSkyHorizon = new Color(0.7f, 0.82f, 0.9f);
        private static readonly Color MistSkyGlow = new Color(0.85f, 0.92f, 0.95f);
        private const float MistSkyBlend = 0.6f;

        /// <summary>The moon's disc: the size the game's own night sky gives it (the shader draws the moon with its sun disc).</summary>
        private const float MoonSize = 0.092f;
        /// <summary>Degrees below the horizon over which the moon's disc grows in as the sun sets (and the sun's goes).</summary>
        private const float MoonRise = 6f;
        /// <summary>The sky's cloud cutoff under a full overcast (the game's skies use 0.81: less cloud). See the test notes.</summary>
        private const float OvercastCloudCutoff = 0.45f;
        /// <summary>City nights: the horizon's warm glow from the streets, and how far the horizon goes to it.</summary>
        private static readonly Color CityGlow = new Color(0.45f, 0.26f, 0.13f);
        private const float CityGlowBlend = 0.25f;

        private readonly LightGreen.Core.MaterialEdits _sky = new LightGreen.Core.MaterialEdits();
        private Material _material;
        private Color _fogColor = Color.grey;
        private SphericalHarmonicsL2 _stockProbe;
        private bool _ambientChanged;
        private Vector4 _lastSkyKey;
        private float _nextAmbientRefresh;
        private float _skyBrightnessDelta, _horizonBrightnessDelta;
        private bool _moonUp;   // this frame, the sky's disc is the moon

        /// <summary>The sky's horizon colour as shown this frame (the hole's fog colour when there's no sky).</summary>
        internal Color HorizonColor { get; private set; } = Color.grey;

        /// <summary>A new scene: its sky material (or none), and its fog colour and ambient light as the game set them.</summary>
        internal void Capture(Material sky, Color fogColor)
        {
            _material = sky;
            _fogColor = fogColor;
            _stockProbe = RenderSettings.ambientProbe;
            HorizonColor = sky != null && sky.HasProperty("_HorizonColor") ? sky.GetColor("_HorizonColor") : fogColor;
        }

        /// <summary>This frame's sky and ambient light, from the time of day, the weather and the brightness.</summary>
        internal void Apply(EnvironmentController env)
        {
            ApplySky(env);
            ApplyAmbient(env);
        }

        private void ApplySky(EnvironmentController env)
        {
            Material sky = _material;
            if (sky == null)
            {
                _skyBrightnessDelta = _horizonBrightnessDelta = 0f;
                return;
            }

            float blend = Mathf.Clamp01(env.Current.skyBlend);
            Blend(sky, "_SkyColor", env.Current.skyColor, blend);
            Blend(sky, "_HorizonColor", env.Current.horizonColor, blend);
            Blend(sky, "_SunGradientColor", env.Current.sunGlowColor, blend);

            // Rain: the sky's colours toward a soft grey. Snow: toward a bright, pale blue, dimmed at night.
            float overcast = Mathf.Clamp01(env.Overcast);
            if (overcast > 0f)
            {
                float snowy = Mathf.Clamp01(env.Snowy);
                float misty = Mathf.Clamp01(env.Misty);
                float grey = OvercastSkyGrey * overcast * (1f - Mathf.Clamp01(snowy + misty));
                Grey(sky, "_SkyColor", grey);
                Grey(sky, "_HorizonColor", grey);
                Grey(sky, "_SunGradientColor", grey);
                float snow = SnowSkyBlend * overcast * snowy;
                if (snow > 0f)
                {
                    float dim = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(env.Darkness));
                    Tint(sky, "_SkyColor", SnowSkyTop * dim, snow);
                    Tint(sky, "_HorizonColor", SnowSkyHorizon * dim, snow);
                    Tint(sky, "_SunGradientColor", SnowSkyGlow * dim, snow);
                }
                float mist = MistSkyBlend * overcast * misty;
                if (mist > 0f)
                {
                    float dim = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(env.Darkness));
                    Tint(sky, "_SkyColor", MistSkyTop * dim, mist);
                    Tint(sky, "_HorizonColor", MistSkyHorizon * dim, mist);
                    Tint(sky, "_SunGradientColor", MistSkyGlow * dim, mist);
                }
            }

            // Weather: thicker cloud in the sky's own clouds; rain and snow most, cartoon rain's mist half as much.
            float cover = overcast * (1f - 0.5f * Mathf.Clamp01(env.Misty));
            BlendFloat(sky, "_Cutoff", OvercastCloudCutoff, cover);

            // City nights: the streets light the horizon a little, warm, with City night's strength.
            if (GameEvents.CurrentBiome == Biome.City)
            {
                float glow = CityGlowBlend * Mathf.Clamp01(env.Darkness) * Mathf.Clamp01(Settings.CityNight.Value);
                if (glow > 0.001f) Tint(sky, "_HorizonColor", CityGlow, glow);
            }

            // Night: the sky's own stars, brighter with Effects.Stars, coming out late at dusk (eased) and hidden by the
            // weather's cloud. 1 is the sky's own night level; the game's own night skies keep theirs, scaled the same.
            float night = Mathf.Clamp01(env.Current.night);
            float starsOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, night));
            float clear = 1f - 0.9f * overcast;
            if (sky.HasProperty("_StarsExposure"))
            {
                float stock = _sky.StockFloat(sky, "_StarsExposure");
                float stars = Mathf.Lerp(stock, 1f, starsOut) * Mathf.Max(0f, Settings.Stars.Value) * clear;
                _sky.SetFloat(sky, "_StarsExposure", stock > 0f || starsOut > 0f ? stars : stock);
            }
            BlendFloat(sky, "_MoonCycle", env.Current.moonPhase, night);

            // The moon: the shader draws it with the sun's disc, shaded by _MoonCycle, as the game's own night skies do. With
            // the sun below the horizon (the scene's light is then the moon), the disc goes where the moonlight comes from,
            // at the moon's size, growing in over the first degrees after sunset, so the swap is never seen.
            // Only under LightGreen's own time of day: the game's own night holes keep their own moon.
            _moonUp = env.SunMoved && env.Sun != null && env.Current.sunWeight > 0f && env.SunElevation < 0f;
            float moon = _moonUp ? Mathf.SmoothStep(0f, 1f, -env.SunElevation / MoonRise) : 0f;
            if (sky.HasProperty("_SunSize"))
                _sky.SetFloat(sky, "_SunSize", _moonUp ? MoonSize * moon : _sky.StockFloat(sky, "_SunSize"));
            // Less of the sun's wide glow at night; a little more kept round a moon that's up, for a soft halo.
            if (sky.HasProperty("_SunScatterIntensity"))
                _sky.SetFloat(sky, "_SunScatterIntensity",
                    _sky.StockFloat(sky, "_SunScatterIntensity") * Mathf.Lerp(Mathf.Lerp(1f, 0.2f, night), 0.35f, moon) * clear);

            // The sky's own change in brightness, for the fill light, before Brightness.Day and Night scale it.
            _skyBrightnessDelta = BrightnessDelta(sky, "_SkyColor");
            _horizonBrightnessDelta = BrightnessDelta(sky, "_HorizonColor");
            if (!EnvironmentController.Unscaled(env.LightScale))
            {
                Scale(sky, "_SkyColor", env.LightScale);
                Scale(sky, "_HorizonColor", env.LightScale);
                Scale(sky, "_SunGradientColor", env.LightScale);
            }

            if (sky.HasProperty("_SunDirection") && env.Sun != null)
            {
                Vector4 stock = _sky.StockVector(sky, "_SunDirection");
                // By day the sun; after sunset the moon (where the moonlight comes from).
                Vector3 toSun = _moonUp ? -env.Sun.transform.forward : env.SunDirection;
                _sky.SetVector(sky, "_SunDirection", env.SunMoved ? new Vector4(toSun.x, toSun.y, toSun.z, stock.w) : stock);
            }

            HorizonColor = sky.HasProperty("_HorizonColor") ? sky.GetColor("_HorizonColor") : _fogColor;
        }

        /// <summary>Multiplies one of the sky's colours (as set this frame) by the scene's brightness.</summary>
        private void Scale(Material sky, string property, float scale)
        {
            if (!sky.HasProperty(property)) return;
            Color c = sky.GetColor(property);
            _sky.SetColor(sky, property, new Color(c.r * scale, c.g * scale, c.b * scale, c.a));
        }

        /// <summary>Moves one of the sky's colours (as set this frame) toward grey of the same brightness, a little darker.</summary>
        private void Grey(Material sky, string property, float amount)
        {
            if (!sky.HasProperty(property)) return;
            Color c = sky.GetColor(property);
            float g = c.grayscale * 0.9f;
            Color value = Color.Lerp(c, new Color(g, g, g, c.a), amount);
            _sky.SetColor(sky, property, value);
        }

        /// <summary>The sky colour as it stands this frame, moved toward target.</summary>
        private void Tint(Material sky, string property, Color target, float amount)
        {
            if (!sky.HasProperty(property)) return;
            Color c = sky.GetColor(property);
            Color value = Color.Lerp(c, target, amount);
            value.a = c.a;
            _sky.SetColor(sky, property, value);
        }

        private void BlendFloat(Material sky, string property, float target, float blend)
        {
            if (!sky.HasProperty(property)) return;
            _sky.SetFloat(sky, property, Mathf.Lerp(_sky.StockFloat(sky, property), target, blend));
        }

        private void Blend(Material sky, string property, Color target, float blend)
        {
            if (!sky.HasProperty(property)) return;
            Color stock = _sky.StockColor(sky, property);
            Color value = Color.Lerp(stock, target, blend);
            value.a = stock.a;
            _sky.SetColor(sky, property, value);
        }

        /// <summary>How much of a sky colour change reaches the ambient (fill) light.</summary>
        private const float AmbientFromSky = 0.5f;

        /// <summary>
        /// Shifts the hole's own ambient light by the change in the sky's brightness, instead of recomputing it
        /// from the sky: the game's ambient isn't what Unity would compute from its sky (recomputing it washed
        /// every toon surface pale), so with the sky at stock the ambient stays exactly stock.
        /// <para>Only the brightness moves, as grey: the fill light keeps the hole's own hue, and the preset's
        /// ambient tint colours it. Shifting by the colour change made snow under a warm horizon turn
        /// yellow-green wherever it faced sideways or down (warm horizon minus the hole's blue one = less blue).</para>
        /// </summary>
        private void ApplyAmbient(EnvironmentController env)
        {
            // Until LightGreen changes it, follow the engine's own value (it can finish updating after load).
            if (!_ambientChanged) _stockProbe = RenderSettings.ambientProbe;

            // A darker sky lowers the fill light only a little; the preset's intensity and tint do the rest.
            float skyDelta = Mathf.Max(_skyBrightnessDelta, -0.2f);
            float horizonDelta = Mathf.Max(_horizonBrightnessDelta, -0.2f);
            // The preset's intensity, and Brightness.Day and Night.
            float scale = Mathf.Max(0f, env.Current.ambientIntensity) * env.LightScale;
            Color tint = env.Current.ambientTint;

            var key = new Vector4(skyDelta, horizonDelta, scale, tint.r + tint.g * 3.1f + tint.b * 7.3f);
            bool unchanged = (key - _lastSkyKey).sqrMagnitude < 1e-8f;
            if (unchanged && (!_ambientChanged || Time.unscaledTime < _nextAmbientRefresh)) return;
            _lastSkyKey = key;
            _nextAmbientRefresh = Time.unscaledTime + 2f; // re-assert now and then in case the engine resets it

            bool stock = Mathf.Abs(skyDelta) < 1e-4f && Mathf.Abs(horizonDelta) < 1e-4f &&
                         Mathf.Approximately(scale, 1f) && tint.r > 0.999f && tint.g > 0.999f && tint.b > 0.999f;
            if (stock)
            {
                if (_ambientChanged) RenderSettings.ambientProbe = _stockProbe;
                _ambientChanged = false;
                return;
            }

            var shift = new SphericalHarmonicsL2();
            shift.Clear();
            shift.AddAmbientLight(Grey(horizonDelta * AmbientFromSky));
            shift.AddDirectionalLight(Vector3.up, Grey((skyDelta - horizonDelta) * AmbientFromSky), 1f);
            // Per colour channel, so a tint (blue at night) colours the fill light.
            SphericalHarmonicsL2 probe = _stockProbe + shift;
            float r = scale * Mathf.Max(0f, tint.r);
            float g = scale * Mathf.Max(0f, tint.g);
            float b = scale * Mathf.Max(0f, tint.b);
            for (int c = 0; c < 9; c++)
            {
                probe[0, c] *= r;
                probe[1, c] *= g;
                probe[2, c] *= b;
            }
            RenderSettings.ambientProbe = probe;
            _ambientChanged = true;
        }

        /// <summary>How much brighter one of the sky's colours is than the hole's own (negative: darker).</summary>
        private float BrightnessDelta(Material sky, string property)
        {
            if (!sky.HasProperty(property)) return 0f;
            return Luminance(sky.GetColor(property)) - Luminance(_sky.StockColor(sky, property));
        }

        /// <summary>Perceived brightness (Rec. 709 weights).</summary>
        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Grey(float v) => new Color(v, v, v, 0f);

        /// <summary>The sky material and (when <paramref name="sameScene"/>) the ambient light back as the game had them.</summary>
        internal void Restore(bool sameScene)
        {
            _sky.Restore();
            if (sameScene && _ambientChanged) RenderSettings.ambientProbe = _stockProbe;
            _ambientChanged = false;
            _lastSkyKey = Vector4.zero;
            _material = null;
        }
    }
}
