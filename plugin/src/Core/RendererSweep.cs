using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightGreen.Core
{
    /// <summary>
    /// Goes through every renderer in the world a few frames at a time: the renderers are found in one call, then
    /// handed out until this frame's time is spent (<see cref="Budget"/>), and the rest wait for the next frame.
    /// Looking at the materials of 5,000-6,000 renderers takes 3-5 ms, a hitch when done in one frame.
    /// </summary>
    internal sealed class RendererSweep
    {
        /// <summary>Time per frame for handing out renderers.</summary>
        internal const double BudgetMs = 0.5;

        private static readonly long Budget = (long)(BudgetMs * Stopwatch.Frequency / 1000.0);

        private Renderer[] _renderers;
        private int _next;
        private long _deadline;
        private long _spent;
        private long _frameStart;

        internal bool Running => _renderers != null;

        /// <summary>Renderers in the sweep in progress, or the last one.</summary>
        internal int Count { get; private set; }

        /// <summary>Frames the last sweep took, and its time in all (finding included), for the log.</summary>
        internal int Frames { get; private set; }

        internal double TotalMs => _spent * 1000.0 / Stopwatch.Frequency;

        internal void Start(FindObjectsInactive inactive)
        {
            long start = Stopwatch.GetTimestamp();
            _renderers = Object.FindObjectsByType<Renderer>(inactive, FindObjectsSortMode.None);
            _next = 0;
            Count = _renderers.Length;
            Frames = 0;
            _spent = Stopwatch.GetTimestamp() - start;
        }

        /// <summary>Starts this frame's share. Call once a frame before <see cref="Next"/>.</summary>
        internal void BeginFrame()
        {
            _frameStart = Stopwatch.GetTimestamp();
            _deadline = _frameStart + Budget;
            Frames++;
        }

        /// <summary>The next renderer, or false when this frame's time is spent or the sweep is done.</summary>
        internal bool Next(out Renderer renderer)
        {
            renderer = null;
            if (_renderers == null) return false;
            // The clock is read every 16 renderers; each takes well under a microsecond to a few.
            if ((_next & 15) == 0 && _next > 0 && Stopwatch.GetTimestamp() > _deadline) return false;
            while (_next < _renderers.Length)
            {
                renderer = _renderers[_next++];
                if (renderer != null) return true;
            }
            return false;
        }

        /// <summary>Ends this frame's share; true when the whole sweep is done (and its renderers let go).</summary>
        internal bool EndFrame()
        {
            _spent += Stopwatch.GetTimestamp() - _frameStart;
            if (_renderers == null || _next < _renderers.Length) return false;
            _renderers = null;
            return true;
        }

        /// <summary>Drops a sweep in progress (a map change, or the feature restored).</summary>
        internal void Stop() => _renderers = null;
    }
}
