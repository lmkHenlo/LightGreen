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
    /// </list>
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
        private static int _parts;

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
        internal static int Part(string name)
        {
            for (int i = 0; i < _parts; i++)
                if (Names[i] == name) return i;
            if (_parts == MaxParts) return MaxParts - 1;   // shares the last one; there are far fewer
            Names[_parts] = name;
            return _parts++;
        }

        internal static long Now => Stopwatch.GetTimestamp();

        /// <summary>Adds the time since <paramref name="start"/> (from <see cref="Now"/>) to a part.</summary>
        internal static void Add(int part, long start) => Ticks[part] += Stopwatch.GetTimestamp() - start;

        /// <summary>
        /// Closes the frame before this one: call first thing in each frame. The time since the last frame
        /// (<see cref="Time.unscaledDeltaTime"/>) is that frame's length, and its parts were added up during it.
        /// </summary>
        internal static void NextFrame()
        {
            long total = 0;
            for (int i = 0; i < _parts; i++) total += Ticks[i];
            double ours = total * MsPerTick;
            double frame = Time.unscaledDeltaTime * 1000.0;

            if (ours > OverMs) _over++;
            if (ours > _worstMs)
            {
                _worstMs = ours;
                _worstFrameMs = frame;
                _worstAt = DateTime.UtcNow;
                Array.Copy(Ticks, WorstTicks, _parts);
            }
            // Long next to the usual frame: 4 ms frames hitch at 10 ms, 16.7 ms ones at 42 ms. The average follows
            // slowly (about 2 s at 240 fps) and isn't pulled up by the long frames themselves.
            double longOver = Math.Max(_usualMs * 2.5, _usualMs + 6.0);
            bool isLong = frame > longOver && Settings.TimingLog.Value;
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
                }
            }
            Array.Clear(Ticks, 0, _parts);

            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + ReportEvery;
            if (_over > 0 || _long > 0) Plugin.Log.LogInfo(Report());
            _worstMs = 0;
            _over = 0;
            _long = 0;
            _longestMs = 0;
        }

        private static string Report()
        {
            var line = new StringBuilder("Timing (last 10 s): ");
            if (_over > 0)
            {
                string parts = string.Join(", ", Enumerable.Range(0, _parts)
                    .Where(i => WorstTicks[i] * MsPerTick >= 0.05)
                    .OrderByDescending(i => WorstTicks[i])
                    .Select(i => $"{Names[i]} {WorstTicks[i] * MsPerTick:0.0}"));
                line.Append($"{_over} frame{(_over == 1 ? "" : "s")} with over {OverMs:0} ms of LightGreen's work; the most " +
                            $"{_worstMs:0.0} ms at {_worstAt:HH:mm:ss}Z, in a {_worstFrameMs:0.0} ms frame ({parts})");
            }
            else line.Append($"LightGreen's work under {OverMs:0} ms in every frame (the most {_worstMs:0.0} ms)");
            if (_long > 0)
                line.Append($"; {_long} long frame{(_long == 1 ? "" : "s")} (over {_longOverMs:0.0} ms; usual {_usualMs:0.0} ms), the longest " +
                            $"{_longestMs:0.0} ms at {_longestAt:HH:mm:ss}Z, LightGreen's share of it {_longestOursMs:0.0} ms");
            return line.Append('.').ToString();
        }
    }
}
