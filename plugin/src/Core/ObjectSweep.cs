using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightGreen.Core
{
    /// <summary>
    /// Goes through every object of one kind in the world a few frames at a time: the objects are found in one call,
    /// then handed out until this frame's time is spent (<see cref="Budget"/>), and the rest wait for the next frame.
    /// Looking at the materials of 5,000-6,000 renderers takes 3-5 ms, a hitch when done in one frame; so does
    /// looking at the names of every transform on a City map.
    /// </summary>
    internal sealed class ObjectSweep<T> where T : Object
    {
        /// <summary>Time per frame for handing out objects (all sweeps running in a frame get this each).</summary>
        internal const double BudgetMs = 0.5;

        private static readonly long Budget = (long)(BudgetMs * Stopwatch.Frequency / 1000.0);

        private T[] _objects;
        private int _next;
        private long _deadline;
        private long _spent;
        private long _frameStart;

        internal bool Running => _objects != null;

        /// <summary>Objects in the sweep in progress, or the last one.</summary>
        internal int Count { get; private set; }

        /// <summary>Frames the last sweep took, and its time in all (finding included), for the log.</summary>
        internal int Frames { get; private set; }

        internal double TotalMs => _spent * 1000.0 / Stopwatch.Frequency;

        internal void Start(FindObjectsInactive inactive)
        {
            long start = Stopwatch.GetTimestamp();
            _objects = Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
            _next = 0;
            Count = _objects.Length;
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

        /// <summary>The next object, or false when this frame's time is spent or the sweep is done.</summary>
        internal bool Next(out T item)
        {
            item = null;
            if (_objects == null) return false;
            // The clock is read every 16 objects; each takes well under a microsecond to a few.
            if ((_next & 15) == 0 && _next > 0 && Stopwatch.GetTimestamp() > _deadline) return false;
            while (_next < _objects.Length)
            {
                item = _objects[_next++];
                if (item != null) return true;
            }
            return false;
        }

        /// <summary>Ends this frame's share; true when the whole sweep is done (and its objects let go).</summary>
        internal bool EndFrame()
        {
            _spent += Stopwatch.GetTimestamp() - _frameStart;
            if (_objects == null || _next < _objects.Length) return false;
            _objects = null;
            return true;
        }

        /// <summary>Drops a sweep in progress (a map change, or the feature restored).</summary>
        internal void Stop() => _objects = null;
    }
}
