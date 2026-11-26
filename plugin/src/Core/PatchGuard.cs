using System;

namespace LightGreen.Core
{
    /// <summary>
    /// Keeps a Harmony patch body from breaking the game's own method. A patch runs inside the game's code, where an
    /// exception doesn't reach the feature host: it would go into the game's update, every frame. Each patch keeps one
    /// guard: the body returns at once while <see cref="Broken"/>, and catches into <see cref="Fail"/>, which logs the
    /// first failure in full and stands the patch down until the game restarts.
    /// </summary>
    /// <example>
    /// <code>
    /// private static readonly PatchGuard Guard = new PatchGuard("HUD markers");
    /// private static void Postfix(...) { if (Guard.Broken) return; try { ... } catch (Exception e) { Guard.Fail(e); } }
    /// </code>
    /// </example>
    internal sealed class PatchGuard
    {
        internal readonly string Label;

        internal PatchGuard(string label) => Label = label;

        /// <summary>True once the body has failed: it does nothing more this session.</summary>
        internal bool Broken { get; private set; }

        internal void Fail(Exception e)
        {
            if (Broken) return;
            Broken = true;
            CoreLog.Source?.LogError($"[FAILED] {Label}: {e}\n{Label} is off until the game restarts; the game's own behaviour is left as it is.");
        }
    }
}
