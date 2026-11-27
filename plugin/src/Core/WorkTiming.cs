using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// Measures LightGreen's own work in each frame, by part (each feature, the HUD sizes, the per-frame
    /// housekeeping), and logs it only when it matters, at most once every 10 s:
    /// <list type="bullet">
    /// <item>frames where LightGreen's work took over 2 ms, with the worst one's parts;</item>
    /// <item>with General.TimingLog on: frames much longer than usual (over 2.5 times the usual frame and 6 ms
    /// more), however they came about, with LightGreen's share of the longest one. With F8 at off, LightGreen does
    /// no feature work, so this also shows what the game's own long frames are like.</item>
    /// <item>with General.TimingLog on: the garbage collections in the 10 s (and whether one fell in the worst or the
    /// longest frame), and the managed heap's size. Mono's collector stops the whole game while it runs.</item>
    /// </list>
    /// A frame's length is measured with LightGreen's own clock, from one frame's start to the next's: around a load
    /// <see cref="Time.unscaledDeltaTime"/> didn't match the work timed in that frame. Steps (<see cref="Step"/>) split a
    /// part into what it did, for one-frame costs whose cause isn't clear; they're shown with the worst frame's parts
    /// and not counted twice in its total.
    /// Costs two clock reads per part per frame; the line is only put together when there's something to say.
    /// </summary>
    internal static class WorkTiming
    {
        private const double OverMs = 2.0;
        private const float ReportEvery = 10f;
        private const int MaxParts = 40;

        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
        private static readonly string[] Names = new string[MaxParts];
        private static readonly long[] Ticks = new long[MaxParts];
        private static readonly long[] WorstTicks = new long[MaxParts];
        private static readonly bool[] IsStep = new bool[MaxParts];
        private static int _parts;

        private static long _frameStart;   // this frame's start, by LightGreen's clock; 0 before the first frame
        private static int _gcSeen = -1;    // GC.CollectionCount(0) at the last frame's start
        private static int _gcs;            // garbage collections in this 10 s
        private static bool _worstHadGc, _longestHadGc;

        private static double _worstMs;
        private static double _worstFrameMs;
        private static DateTime _worstAt;
        private static int _over;
        private static int _long;
        private static double _longestMs;
        private static double _longestOursMs;
        private static DateTime _longestAt;
        private static float _nextReport = ReportEvery;
        private static double _usualMs = 16.7;   // a slow-moving average of the frame time
        private static double _longOverMs;

        /// <summary>A part to time, by the name the log uses. Call once, at start.</summary>
        internal static int Part(string name) => Register(name, false);

        /// <summary>
        /// A step inside a part ("Time of day: sun search"), shown with the worst frame's parts but not added to its
        /// total, since its part already counts it. Call once, at start.
        /// </summary>
        internal static int Step(string name) => Register(name, true);

        private static int Register(string name, bool step)
        {
            for (int i = 0; i < _parts; i++)
                if (Names[i] == name) return i;
            if (_parts == MaxParts) return MaxParts - 1;   // shares the last one; there are far fewer
            Names[_parts] = name;
            IsStep[_parts] = step;
            return _parts++;
        }

        internal static long Now => Stopwatch.GetTimestamp();

        /// <summary>General.TimingLog: also log frames much longer than usual, whatever caused them. Set by the plugin.</summary>
        internal static bool LogLongFrames;

        /// <summary>Adds the time since <paramref name="start"/> (from <see cref="Now"/>) to a part.</summary>
        internal static void Add(int part, long start) => Ticks[part] += Stopwatch.GetTimestamp() - start;

        /// <summary>Adds the time since <paramref name="start"/> to a step and returns now, the next step's start.</summary>
        internal static long Lap(int step, long start)
        {
            long now = Stopwatch.GetTimestamp();
            Ticks[step] += now - start;
            return now;
        }

        /// <summary>
        /// Closes the frame before this one: call first thing in each frame. The time since the last call is that
        /// frame's length, its parts were added up during it, and a garbage collection counted since then fell in it.
        /// </summary>
        internal static void NextFrame()
        {
            long now = Stopwatch.GetTimestamp();
            double frame = _frameStart != 0 ? (now - _frameStart) * MsPerTick : Time.unscaledDeltaTime * 1000.0;
            _frameStart = now;

            int gcCount = GC.CollectionCount(0);
            bool gc = _gcSeen >= 0 && gcCount != _gcSeen;
            if (gc) _gcs += gcCount - _gcSeen;
            _gcSeen = gcCount;

            long total = 0;
            for (int i = 0; i < _parts; i++)
                if (!IsStep[i]) total += Ticks[i];
            double ours = total * MsPerTick;

            if (ours > OverMs) _over++;
            if (ours > _worstMs)
            {
                _worstMs = ours;
                _worstFrameMs = frame;
                _worstAt = DateTime.UtcNow;
                _worstHadGc = gc;
                Array.Copy(Ticks, WorstTicks, _parts);
            }
            // Long next to the usual frame: 4 ms frames hitch at 10 ms, 16.7 ms ones at 42 ms. The average follows
            // slowly (about 2 s at 240 fps) and isn't pulled up by the long frames themselves.
            double longOver = Math.Max(_usualMs * 2.5, _usualMs + 6.0);
            bool isLong = frame > longOver && LogLongFrames;
            if (frame <= longOver && frame > 0) _usualMs += (frame - _usualMs) * 0.002;
            if (isLong)
            {
                _long++;
                if (frame > _longestMs)
                {
                    _longestMs = frame;
                    _longestOursMs = ours;
                    _longestAt = DateTime.UtcNow;
                    _longOverMs = longOver;
                    _longestHadGc = gc;
                }
            }
            Array.Clear(Ticks, 0, _parts);

            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + ReportEvery;
            if (_over > 0 || _long > 0 || (LogLongFrames && _gcs > 0)) CoreLog.Source?.LogInfo(Report());
            _worstMs = 0;
            _over = 0;
            _long = 0;
            _longestMs = 0;
            _gcs = 0;
            _worstHadGc = _longestHadGc = false;
        }

        private static string Report()
        {
            var line = new StringBuilder("Timing (last 10 s): ");
            if (_over > 0)
            {
                string parts = Listed(false);
                string steps = Listed(true);
                line.Append($"{_over} frame{(_over == 1 ? "" : "s")} with over {OverMs:0} ms of LightGreen's work; the most " +
                            $"{_worstMs:0.0} ms at {_worstAt:HH:mm:ss}Z, in a {_worstFrameMs:0.0} ms frame ({parts})" +
                            (steps.Length > 0 ? $" [steps: {steps}]" : "") +
                            (_worstHadGc && LogLongFrames ? ", with a garbage collection" : ""));
            }
            else line.Append($"LightGreen's work under {OverMs:0} ms in every frame (the most {_worstMs:0.0} ms)");
            if (_long > 0)
                line.Append($"; {_long} long frame{(_long == 1 ? "" : "s")} (over {_longOverMs:0.0} ms; usual {_usualMs:0.0} ms), the longest " +
                            $"{_longestMs:0.0} ms at {_longestAt:HH:mm:ss}Z, LightGreen's share of it {_longestOursMs:0.0} ms" +
                            (_longestHadGc ? ", with a garbage collection" : ""));
            if (LogLongFrames)
                line.Append($"; {_gcs} garbage collection{(_gcs == 1 ? "" : "s")}, managed heap {GC.GetTotalMemory(false) / (1024.0 * 1024.0):0} MB");
            return line.Append('.').ToString();
        }

        /// <summary>The worst frame's parts (or steps) over 0.05 ms, largest first.</summary>
        private static string Listed(bool steps) => string.Join(", ", Enumerable.Range(0, _parts)
            .Where(i => IsStep[i] == steps && WorstTicks[i] * MsPerTick >= 0.05)
            .OrderByDescending(i => WorstTicks[i])
            .Select(i => $"{Names[i]} {WorstTicks[i] * MsPerTick:0.0}"));
    }
}
