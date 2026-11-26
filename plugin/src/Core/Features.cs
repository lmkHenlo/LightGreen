using System;
using System.Collections.Generic;

namespace LightGreen.Core
{
    /// <summary>
    /// One visual feature. <see cref="Apply"/> runs every frame while LightGreen is on. <see cref="Restore"/> puts
    /// the game back exactly as it was; it must be safe to call at any time and any number of times, including
    /// when the feature never applied anything.
    /// </summary>
    internal interface IFeature
    {
        /// <summary>The name the log uses for it.</summary>
        string Name { get; }

        void Apply();

        void Restore();
    }

    /// <summary>
    /// A feature that holds things from the map or hole it's on (materials, objects, prints) and needs to hear when
    /// that changes. <see cref="FeatureHost"/> tells it, whether or not LightGreen is showing at the time.
    /// </summary>
    internal interface IMapAware
    {
        /// <summary>A different map (biome): let go of the old one's things, and look for the new one's.</summary>
        void OnBiomeChanged();

        /// <summary>The next hole on the same map.</summary>
        void OnHoleChanged();
    }

    /// <summary>
    /// Runs the features. A feature that throws is restored and switched off until the game restarts, and the
    /// rest carry on, so one effect broken by a game update can't take the others (or the game) down with it.
    /// </summary>
    internal sealed class FeatureHost
    {
        private readonly List<IFeature> _features = new List<IFeature>();
        private readonly List<int> _timing = new List<int>();   // each feature's part in WorkTiming
        private readonly HashSet<IFeature> _failed = new HashSet<IFeature>();
        private bool _applied;

        internal int Count => _features.Count;

        internal void Add(IFeature feature)
        {
            _features.Add(feature);
            _timing.Add(WorkTiming.Part(feature.Name));
        }

        /// <summary>Each working feature, once a frame while LightGreen is on.</summary>
        internal void Apply()
        {
            _applied = true;
            for (int i = 0; i < _features.Count; i++)
            {
                IFeature feature = _features[i];
                if (_failed.Contains(feature)) continue;
                long start = WorkTiming.Now;
                try
                {
                    feature.Apply();
                }
                catch (Exception e)
                {
                    _failed.Add(feature);
                    CoreLog.Source?.LogError($"[FAILED] {feature.Name}: {e}\n{feature.Name} is off until the game restarts; " +
                                        "the rest of LightGreen carries on.");
                    Restore(feature);
                }
                WorkTiming.Add(_timing[i], start);
            }
        }

        /// <summary>The game's own look back (LightGreen off, or the ToggleKey comparison). Only does work once.</summary>
        internal void Restore()
        {
            if (!_applied) return;
            _applied = false;
            foreach (IFeature feature in _features) Restore(feature);
        }

        /// <summary>Tells each working <see cref="IMapAware"/> feature the map changed; one that throws is switched off.</summary>
        internal void OnBiomeChanged() => Tell(true);

        /// <summary>Tells each working <see cref="IMapAware"/> feature the hole changed.</summary>
        internal void OnHoleChanged() => Tell(false);

        private void Tell(bool biome)
        {
            foreach (IFeature feature in _features)
            {
                if (!(feature is IMapAware aware) || _failed.Contains(feature)) continue;
                try
                {
                    if (biome) aware.OnBiomeChanged();
                    else aware.OnHoleChanged();
                }
                catch (Exception e)
                {
                    _failed.Add(feature);
                    CoreLog.Source?.LogError($"[FAILED] {feature.Name} (on a {(biome ? "map" : "hole")} change): {e}\n" +
                                             $"{feature.Name} is off until the game restarts; the rest of LightGreen carries on.");
                    Restore(feature);
                }
            }
        }

        /// <summary>Every feature back, whatever state it's in (shutdown).</summary>
        internal void RestoreAll()
        {
            _applied = false;
            foreach (IFeature feature in _features) Restore(feature);
        }

        private static void Restore(IFeature feature)
        {
            try
            {
                feature.Restore();
            }
            catch (Exception e)
            {
                CoreLog.Source?.LogError($"{feature.Name} couldn't put the game back as it was: {e}");
            }
        }
    }
}
