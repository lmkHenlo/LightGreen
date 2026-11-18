using System.Collections.Generic;
using LightGreen.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LightGreen.Systems
{
    /// <summary>
    /// Sharper and longer sun shadows: a bigger shadow map, a longer shadow distance and cascade splits
    /// re-spread over it. The game swaps its pipeline asset when graphics settings change, so this
    /// follows whichever asset is active and applies again. Each asset's stock values are remembered and
    /// put back on Restore. Nothing is written to disk.
    /// </summary>
    internal sealed class SunShadows : IFeature
    {
        public string Name => "Sun shadows";

        /// <summary>On a hole; off a hole, the game's own values.</summary>
        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            Update();
        }

        private sealed class Stock
        {
            internal int Resolution;
            internal float Distance;
            internal float Split2;
            internal Vector2 Split3;
            internal Vector3 Split4;
        }

        private readonly Dictionary<UniversalRenderPipelineAsset, Stock> _stock = new Dictionary<UniversalRenderPipelineAsset, Stock>();
        private UniversalRenderPipelineAsset _appliedTo;
        private int _appliedResolution = -1;
        private int _appliedResolutionValue = -1;
        private float _appliedDistance = -1f;

        /// <summary>
        /// The game re-applied its graphics settings: it sets the shadow map size from its own Shadow
        /// quality option, so that becomes the stock size, and LightGreen applies again on the next Update.
        /// </summary>
        internal void OnGameApplied()
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset != null && _stock.TryGetValue(asset, out Stock stock) &&
                !(asset == _appliedTo && asset.mainLightShadowmapResolution == _appliedResolutionValue))
                stock.Resolution = asset.mainLightShadowmapResolution;
            _appliedTo = null;
        }

        private void Update()
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset == null) return;
            int resolution = Settings.SunShadowResolution.Value;
            float distance = Settings.SunShadowDistance.Value;
            if (asset == _appliedTo && resolution == _appliedResolution && Mathf.Approximately(distance, _appliedDistance)) return;

            if (!_stock.TryGetValue(asset, out Stock stock))
            {
                stock = new Stock
                {
                    Resolution = asset.mainLightShadowmapResolution,
                    Distance = asset.shadowDistance,
                    Split2 = asset.cascade2Split,
                    Split3 = asset.cascade3Split,
                    Split4 = asset.cascade4Split,
                };
                _stock[asset] = stock;
            }

            asset.mainLightShadowmapResolution = resolution > 0 ? Mathf.Max(resolution, stock.Resolution) : stock.Resolution;
            if (distance > 0f && distance > stock.Distance)
            {
                asset.shadowDistance = distance;
                // Keep the nearest cascade about as many metres deep as the game's, so close shadows stay
                // as sharp as before while the far ones reach further.
                float ratio = stock.Distance / distance;
                asset.cascade2Split = Mathf.Clamp(stock.Split2 * ratio, 0.02f, 0.9f);
                asset.cascade3Split = new Vector2(Mathf.Clamp(stock.Split3.x * ratio, 0.02f, 0.45f), Mathf.Clamp(stock.Split3.y * ratio, 0.05f, 0.9f));
                asset.cascade4Split = new Vector3(
                    Mathf.Clamp(stock.Split4.x * ratio, 0.02f, 0.3f),
                    Mathf.Clamp(stock.Split4.y * Mathf.Sqrt(ratio), 0.05f, 0.6f),
                    Mathf.Clamp(stock.Split4.z * Mathf.Sqrt(ratio), 0.1f, 0.9f));
            }
            else
            {
                RestoreSplits(asset, stock);
            }

            if (_appliedTo != asset || resolution != _appliedResolution || !Mathf.Approximately(distance, _appliedDistance))
                Plugin.LogChange("shadows:" + asset.name, $"Sun shadows on {asset.name}: {asset.mainLightShadowmapResolution} px, {asset.shadowDistance:0} m " +
                                   $"(game: {stock.Resolution} px, {stock.Distance:0} m).");
            _appliedTo = asset;
            _appliedResolution = resolution;
            _appliedResolutionValue = asset.mainLightShadowmapResolution;
            _appliedDistance = distance;
        }

        private static void RestoreSplits(UniversalRenderPipelineAsset asset, Stock stock)
        {
            asset.shadowDistance = stock.Distance;
            asset.cascade2Split = stock.Split2;
            asset.cascade3Split = stock.Split3;
            asset.cascade4Split = stock.Split4;
        }

        public void Restore()
        {
            if (_stock.Count == 0) return;
            foreach (KeyValuePair<UniversalRenderPipelineAsset, Stock> pair in _stock)
            {
                if (pair.Key == null) continue;
                pair.Key.mainLightShadowmapResolution = pair.Value.Resolution;
                RestoreSplits(pair.Key, pair.Value);
            }
            _stock.Clear();
            _appliedTo = null;
            _appliedResolution = -1;
            _appliedDistance = -1f;
        }
    }
}
