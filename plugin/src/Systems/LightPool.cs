using System.Collections.Generic;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>One light someone wants this frame.</summary>
    internal struct LightRequest
    {
        internal Vector3 Position;
        internal Color Color;
        internal float Intensity;
        internal float Range;
        /// <summary>Asks for a shadow; only the most important few get one.</summary>
        internal bool Shadows;
        /// <summary>How much it matters, before distance: higher wins a light when there are too many.</summary>
        internal float Priority;
        /// <summary>0 for a point light; otherwise a spot light this wide (degrees), pointing straight down.</summary>
        internal float SpotAngle;
    }

    /// <summary>
    /// A fixed set of URP lights (point, or spot pointing down) that LightGreen hands out each frame: explosions,
    /// muzzle flashes, rocket trails, lightning and City lampposts ask for one during the frame, and
    /// this feature, run after all of them, gives the lights to the most important (by priority, then nearness to
    /// the camera and brightness). The few most important that ask for it cast shadows through URP's extra-light
    /// shadow atlas. Lights nobody asked for are switched off. The game's own lights are never touched.
    ///
    /// URP only draws these with the game's Dynamic Lighting setting on (its other pipeline assets have no extra
    /// lights); with it off they cost nothing and show nothing.
    /// </summary>
    internal sealed class LightPool : IFeature
    {
        /// <summary>Lights at most at once, and shadowed lights at most (each point-light shadow costs six atlas slices).</summary>
        private const int Size = 24;
        private const int MaxShadows = 2;
        private const float ShadowStrength = 0.85f;

        private readonly List<LightRequest> _requests = new List<LightRequest>();
        private readonly Light[] _lights = new Light[Size];
        private GameObject _root;
        private bool _anyOn;

        public string Name => "Light pool";

        /// <summary>Asks for a light this frame. Ignored outside a hole or with LightGreen off.</summary>
        internal void Request(LightRequest request)
        {
            if (request.Intensity > 0.001f && request.Range > 0.01f) _requests.Add(request);
        }

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            if (_requests.Count == 0 && !_anyOn) return;
            EnsureLights();

            Camera camera = GameManager.Camera;
            Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
            // Most important first: priority, then how near and how bright.
            _requests.Sort((a, b) => Score(b, eye).CompareTo(Score(a, eye)));

            int shadows = 0;
            _anyOn = false;
            for (int i = 0; i < Size; i++)
            {
                Light light = _lights[i];
                if (light == null) continue;
                if (i >= _requests.Count)
                {
                    if (light.enabled) light.enabled = false;
                    continue;
                }
                LightRequest r = _requests[i];
                light.transform.position = r.Position;
                LightType type = r.SpotAngle > 0f ? LightType.Spot : LightType.Point;
                if (light.type != type) light.type = type;
                if (type == LightType.Spot)
                {
                    light.spotAngle = r.SpotAngle;
                    light.innerSpotAngle = r.SpotAngle * 0.6f;
                    light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                }
                light.color = r.Color;
                light.intensity = r.Intensity;
                light.range = r.Range;
                bool shadowed = r.Shadows && shadows < MaxShadows;
                if (shadowed) shadows++;
                LightShadows wanted = shadowed ? LightShadows.Soft : LightShadows.None;
                if (light.shadows != wanted) light.shadows = wanted;
                if (!light.enabled) light.enabled = true;
                _anyOn = true;
            }
            _requests.Clear();
        }

        private static float Score(LightRequest r, Vector3 eye) =>
            r.Priority * 1000f + r.Intensity * r.Range / (1f + Vector3.Distance(r.Position, eye) / 30f);

        private void EnsureLights()
        {
            if (_root != null) return;
            _root = new GameObject("LightGreen lights") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(_root);
            for (int i = 0; i < Size; i++)
            {
                var go = new GameObject($"LightGreen light {i}") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(_root.transform, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.shadowStrength = ShadowStrength;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;
                _lights[i] = light;
            }
        }

        /// <summary>Every pooled light off.</summary>
        public void Restore()
        {
            _requests.Clear();
            if (!_anyOn) return;
            _anyOn = false;
            foreach (Light light in _lights)
                if (light != null) light.enabled = false;
        }

        internal void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }
}
