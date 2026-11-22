using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LightGreen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightGreen.Systems
{
    /// <summary>
    /// The [HUD] section: each HUD group drawn at its own size, 0–100 %, 0 hiding it. Only you see it.
    /// <para>
    /// Screen elements are found from the game's HUD components in one pass over the HUD's own canvases, a few times
    /// after each scene or hole loads (never on a timer: a search of the whole scene once a second made the game hitch),
    /// and just before the HUD is drawn each frame (<see cref="Canvas.willRenderCanvases"/>, after the game has moved and animated it) each is scaled toward
    /// the side of the screen it's anchored to (<see cref="HudMath.Edge"/>): the item bar keeps its bottom-right
    /// corner, the progress bar its top-left. Elements that follow something on screen (crosshairs, lock-on markers)
    /// shrink about their own centre instead. What the game sets is read back every frame before it's scaled, so its
    /// own animations (the power meter's pop, the angle arc sliding to the centre) carry on, at the new size.
    /// </para>
    /// <para>
    /// A group can sit inside another (the stroke count and wind inside the progress bar, the swing wind inside the power
    /// meter). The outer one is scaled whole, its own graphics with it; the inner one is placed as if the outer one were
    /// at the game's size, then set against the outer one as scaled, so each keeps its own size and place. A container
    /// stretched over the whole screen isn't scaled itself: its children are, each toward its own side.
    /// </para>
    /// <para>
    /// The markers over the hole, balls and teammates, the name tags and the pop-up words are sized by the game every
    /// frame from their distance; those are scaled in patches (<see cref="HudPatches"/>) right after the game sizes them.
    /// </para>
    /// The lock-on markers and giant-form timers come and go; they're the children of their managers, looked at each
    /// frame. The hole's beam and the aim line aren't changed. Restore puts every element back as the game last set it.
    /// </summary>
    internal sealed class HudScaler
    {
        /// <summary>Whether the [HUD] sizes apply: LightGreen on and ToggleKey at on or HUD only.</summary>
        internal static bool Active;

        /// <summary>A HUD group's scale now: its setting while active, 1 otherwise.</summary>
        internal static float Scale(HudPart part) => Active ? HudMath.Scale(Settings.Hud[(int)part].Value) : 1f;

        /// <summary>Whether a HUD group is hidden now (its setting at 0 while active).</summary>
        internal static bool Hides(HudPart part) => Active && Settings.Hud[(int)part].Value <= 0;

        /// <summary>One element to scale, its group, and the other group's element it sits inside, if any.</summary>
        private struct Target
        {
            internal Transform Piece;
            internal HudPart Part;
            internal Transform Outer;   // the nearest other group's element above it, scaled before it; null if none
            internal HudPart OuterPart; // that element's group
            internal bool AboutPivot;   // shrinks about its own pivot (it follows something on screen)
            internal int Depth;         // how deep in the hierarchy: outer elements are scaled first
        }

        /// <summary>A piece as the game last set it, and as LightGreen last set it.</summary>
        private struct Held
        {
            internal Vector3 BasePosition, BaseScale, Position, Scale;
        }

        private readonly List<Target> _targets = new List<Target>();
        private readonly Dictionary<Transform, Held> _held = new Dictionary<Transform, Held>();
        /// <summary>Containers whose children come and go (lock-on markers, giant-form timers), and their group.</summary>
        private readonly List<KeyValuePair<HudPart, Transform>> _containers = new List<KeyValuePair<HudPart, Transform>>();
        private readonly HashSet<int> _logged = new HashSet<int>();

        /// <summary>After a scene or hole loads the HUD may still be building: looked at again after these delays.</summary>
        private static readonly float[] ScanDelays = { 0.2f, 1f, 3f, 6f };
        private int _scansLeft;
        private float _nextScan;
        private float _lastScan = -10f;
        private bool _wasActive;
        private bool _hooked;
        private bool _broken;

        internal void Hook()
        {
            if (_hooked) return;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            _hooked = true;
        }

        internal void Unhook()
        {
            if (!_hooked) return;
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            _hooked = false;
            RestoreAll();
        }

        /// <summary>Looks for the HUD again, a few times over the next seconds (a new scene or hole).</summary>
        internal void Rescan()
        {
            _scansLeft = ScanDelays.Length;
            _nextScan = Time.unscaledTime + ScanDelays[0];
        }

        private static readonly int Timing = WorkTiming.Part("HUD sizes");

        private void OnWillRenderCanvases()
        {
            if (_broken) return;
            long start = WorkTiming.Now;
            try
            {
                if (!Active)
                {
                    if (_held.Count > 0) RestoreAll();
                    _wasActive = false;
                    return;
                }
                if (!_wasActive)
                {
                    _wasActive = true;
                    Rescan();
                    _nextScan = 0f;
                }
                if (Time.unscaledTime >= _nextScan) Scan();

                bool gone = false;
                foreach (Target target in _targets)
                {
                    if (target.Piece == null) gone = true;
                    else Apply(target);
                }
                foreach (KeyValuePair<HudPart, Transform> container in _containers)
                {
                    if (container.Value == null) { gone = true; continue; }
                    foreach (Transform child in container.Value)
                        Apply(new Target { Piece = child, Part = container.Key, AboutPivot = true });
                }
                // Something was destroyed: look again, at most every two seconds.
                if (gone && _scansLeft == 0 && Time.unscaledTime >= _lastScan + 2f) _nextScan = 0f;
            }
            catch (Exception e)
            {
                _broken = true;
                Plugin.Log.LogError($"[FAILED] HUD sizes: {e}\nThe HUD is back at the game's size until the game restarts.");
                try { RestoreAll(); } catch { /* nothing more to do */ }
            }
            finally
            {
                WorkTiming.Add(Timing, start);
            }
        }

        // ---- Finding the HUD -------------------------------------------------------------------------------

        private readonly struct Root
        {
            internal readonly HudPart Part;
            internal readonly Transform Transform;
            internal readonly bool AboutPivot;
            internal readonly bool Own;   // its own piece even inside another element of the same group
            internal readonly bool Whole; // scaled whole even if it stretches over the screen

            internal Root(HudPart part, Transform transform, bool aboutPivot, bool own = false, bool whole = false)
            {
                Part = part;
                Transform = transform;
                AboutPivot = aboutPivot;
                Own = own;
                Whole = whole;
            }
        }

        private void Scan()
        {
            _lastScan = Time.unscaledTime;
            if (_scansLeft > 0) _scansLeft--;
            _nextScan = _scansLeft > 0
                ? Time.unscaledTime + ScanDelays[ScanDelays.Length - _scansLeft] - ScanDelays[ScanDelays.Length - _scansLeft - 1]
                : float.PositiveInfinity;

            _containers.Clear();
            List<Root> roots = FindRoots(_containers);
            roots = Expand(Deduplicate(roots));

            var targets = new List<Target>();
            var claimed = new HashSet<Transform>();
            var partOf = new Dictionary<Transform, HudPart>();
            foreach (Root root in roots)
            {
                claimed.Add(root.Transform);
                partOf[root.Transform] = root.Part;
            }
            foreach (Root root in roots)
                targets.Add(new Target
                {
                    Piece = root.Transform, Part = root.Part, AboutPivot = root.AboutPivot,
                    Outer = Outer(root.Transform, claimed, partOf, out HudPart outerPart), OuterPart = outerPart,
                    Depth = Depth(root.Transform),
                });
            // Outer elements first: an inner one is placed against its outer one as scaled this frame.
            targets.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            // Pieces no longer scaled (a group split differently, or gone) go back as the game had them.
            var now = new HashSet<Transform>();
            foreach (Target target in targets) now.Add(target.Piece);
            var stale = new List<Transform>();
            foreach (Transform piece in _held.Keys)
                if (piece == null || !now.Contains(piece)) stale.Add(piece);
            foreach (Transform piece in stale) Restore(piece);

            _targets.Clear();
            _targets.AddRange(targets);

            foreach (Root root in roots)
                if (_logged.Add(root.Transform.GetInstanceID()))
                    Plugin.Log.LogInfo($"HUD: {root.Part} is {Path(root.Transform)}.");
        }

        /// <summary>
        /// Each HUD group's element(s), from the game's HUD components, in one pass over the HUD's canvases (the gameplay
        /// screen-space UI; on the Driving Range, its own copy). The containers of the pieces that come and go are added
        /// to <paramref name="containers"/>.
        /// </summary>
        private static List<Root> FindRoots(List<KeyValuePair<HudPart, Transform>> containers)
        {
            var roots = new List<Root>();
            void Add(HudPart part, Transform transform, bool aboutPivot = false, bool own = false, bool whole = false)
            {
                if (transform != null) roots.Add(new Root(part, transform, aboutPivot, own, whole));
            }
            void AddAll(HudPart part, IEnumerable<Transform> transforms)
            {
                foreach (Transform transform in transforms) Add(part, transform);
            }

            var winds = new List<WindInfoUi>();
            var bars = new List<Transform>();
            foreach (Transform canvas in HudCanvases())
                foreach (MonoBehaviour behaviour in canvas.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    switch (behaviour)
                    {
                        case HoleProgressBarUi bar:
                            // The top-left corner: the bar, and the wind readout and stroke count inside it, as one.
                            Add(HudPart.ProgressBar, bar.transform);
                            bars.Add(bar.transform);
                            break;
                        case WindInfoUi wind:
                            winds.Add(wind);
                            break;
                        case RestartPrompt prompt:
                            Add(HudPart.RestartPrompt, prompt.transform);
                            break;
                        case Hotkeys hotkeys:
                            Add(HudPart.ItemBar, Member(hotkeys, "inventoryGroup"));
                            Add(HudPart.ItemBar, Member(hotkeys, "inventoryPitchGroup"));
                            Add(HudPart.ItemBar, Member(hotkeys, "cycleLeftButtonPrompt"));
                            Add(HudPart.ItemBar, Member(hotkeys, "cycleRightButtonPrompt"));
                            AddAll(HudPart.ItemBar, Members(hotkeys, "hotkeyUis"));
                            AddAll(HudPart.ItemBar, Members(hotkeys, "golfCartSeatIcons"));
                            Add(HudPart.AngleSelector, Member(hotkeys, "swingGroup"));
                            Add(HudPart.AngleSelector, Member(hotkeys, "swingButtonPromptsGroup"));
                            AddAll(HudPart.AngleSelector, Members(hotkeys, "swingButtonPrompts"));
                            break;
                        case SwingPitchUi pitch:
                            // The angle arc slides between its corner and the middle of the screen: its own piece, about
                            // its own pivot, so it still reaches the middle.
                            Add(HudPart.AngleSelector, pitch.transform, aboutPivot: true, own: true);
                            break;
                        case SwingPowerBarUi power:
                            Add(HudPart.PowerMeter, power.transform);
                            break;
                        case ButtonPromptManager prompts:
                            Add(HudPart.ControlPrompts, Member(prompts, "leftCorner"));
                            Add(HudPart.ControlPrompts, Member(prompts, "center"));
                            break;
                        case TextChatUi chat: Add(HudPart.Chat, chat.transform); break;
                        case InfoFeed feed: Add(HudPart.KillFeed, feed.transform); break;
                        case Scoreboard board: Add(HudPart.Scoreboard, board.transform); break;
                        case VoiceChatIndicatorsHud voice: Add(HudPart.VoiceChat, voice.transform); break;
                        case HoleInfoUi banner: Add(HudPart.HoleBanner, banner.transform); break;
                        case GameObjectiveMessageUi objective: Add(HudPart.ObjectiveMessage, objective.transform); break;
                        case TeamProgressUi team: Add(HudPart.TeamScore, team.transform); break;
                        case SpeedBoostWheelUi wheel: Add(HudPart.SpeedBoost, wheel.transform); break;
                        case SpectatorButtonPrompts spectator: Add(HudPart.SpectatorPrompts, spectator.transform); break;
                        // The wheel shrinks about the middle of the screen as a whole, not option by option.
                        case QuickChatUi quickChat: Add(HudPart.QuickChat, quickChat.transform, whole: true); break;
                        // These follow a target on screen: about their own pivot.
                        case Reticle reticle: Add(HudPart.Reticles, reticle.transform, aboutPivot: true); break;
                        case TargetReticleUi target: Add(HudPart.Reticles, target.transform, aboutPivot: true); break;
                        // These come and go: their managers' children, looked at each frame.
                        case LockOnTargetUiManager lockOn:
                            containers.Add(new KeyValuePair<HudPart, Transform>(HudPart.Reticles, lockOn.transform));
                            break;
                        case TutorialPromptUi hint: Add(HudPart.Hints, hint.transform); break;
                        case TutorialObjectiveUi objective: Add(HudPart.Hints, objective.transform); break;
                        case JumboBurgerGiantFormTimerManager timers:
                            containers.Add(new KeyValuePair<HudPart, Transform>(HudPart.GiantTimer, timers.transform));
                            break;
                    }
                }

            // Two wind readouts: the top-left one, inside the progress bar and sized with it, and the one beside the power
            // meter.
            foreach (WindInfoUi wind in winds)
            {
                bool inBar = false;
                foreach (Transform bar in bars)
                    if (wind.transform.IsChildOf(bar)) inBar = true;
                if (!inBar) Add(HudPart.SwingWind, wind.transform);
            }
            return roots;
        }

        /// <summary>The HUD's canvases: the gameplay screen-space UI (the one shown, or the Driving Range's).</summary>
        private static IEnumerable<Transform> HudCanvases()
        {
            var seen = new HashSet<Transform>();
            foreach (GameplayScreenspaceUiCanvas canvas in
                     Object.FindObjectsByType<GameplayScreenspaceUiCanvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (canvas != null && seen.Add(canvas.transform.root)) yield return canvas.transform.root;
            if (seen.Count > 0) yield break;
            // Not found by its component: the progress bar's canvas instead.
            foreach (HoleProgressBarUi bar in Object.FindObjectsByType<HoleProgressBarUi>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (bar != null && seen.Add(bar.transform.root)) yield return bar.transform.root;
        }

        /// <summary>Drops an element found twice, or inside another element of the same group.</summary>
        private static List<Root> Deduplicate(List<Root> roots)
        {
            var kept = new List<Root>();
            var seen = new HashSet<Transform>();
            foreach (Root root in roots)
            {
                if (!seen.Add(root.Transform)) continue;
                bool inside = false;
                if (!root.Own)
                    foreach (Root other in roots)
                        if (other.Part == root.Part && other.Transform != root.Transform && root.Transform.IsChildOf(other.Transform))
                        {
                            inside = true;
                            break;
                        }
                if (!inside) kept.Add(root);
            }
            return kept;
        }

        /// <summary>
        /// A container stretched over its whole parent (the screen, usually) isn't scaled itself, or everything in it
        /// would slide toward the middle: its children are, each toward its own side. Up to three levels deep.
        /// </summary>
        private static List<Root> Expand(List<Root> roots)
        {
            var claimed = new HashSet<Transform>();
            foreach (Root root in roots) claimed.Add(root.Transform);
            var result = new List<Root>();
            foreach (Root root in roots)
            {
                if (root.Whole) result.Add(root);
                else Expand(root.Part, root.Transform, root.AboutPivot, 0, claimed, result);
            }
            return result;
        }

        private static void Expand(HudPart part, Transform transform, bool aboutPivot, int depth, HashSet<Transform> claimed, List<Root> result)
        {
            if (depth < 3 && transform is RectTransform rect && transform.childCount > 0 &&
                HudMath.Stretched(rect.anchorMin.x, rect.anchorMax.x) && HudMath.Stretched(rect.anchorMin.y, rect.anchorMax.y))
            {
                // A child that is another group's element is that group's.
                foreach (Transform child in transform)
                    if (!claimed.Contains(child)) Expand(part, child, aboutPivot, depth + 1, claimed, result);
                return;
            }
            result.Add(new Root(part, transform, aboutPivot));
        }

        /// <summary>The nearest other group's element above this one, or null.</summary>
        private static Transform Outer(Transform transform, HashSet<Transform> claimed, Dictionary<Transform, HudPart> partOf,
            out HudPart part)
        {
            part = default;
            for (Transform t = transform.parent; t != null; t = t.parent)
                if (claimed.Contains(t))
                {
                    part = partOf[t];
                    return t;
                }
            return null;
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;
            for (Transform t = transform.parent; t != null; t = t.parent) depth++;
            return depth;
        }

        // ---- Scaling ---------------------------------------------------------------------------------------

        /// <summary>
        /// Scales one element toward its still point. An element inside another group's element (the stroke count
        /// and wind inside the progress bar, the swing wind inside the power meter) moves with the outer one, which is
        /// scaled whole, but keeps its own size: its still point goes where the outer one's scaling takes it, and its
        /// size is undone back to its own setting. With the outer one hidden there's nothing to follow, and it stays where
        /// it is with the outer one at the game's size.
        /// </summary>
        private void Apply(Target target)
        {
            Transform piece = target.Piece;
            if (piece == null) return;
            float k = Scale(target.Part);
            bool outerScaled = target.Outer != null && _held.TryGetValue(target.Outer, out _);

            Vector3 position = piece.localPosition, scale = piece.localScale;
            bool known = _held.TryGetValue(piece, out Held held);
            if (!known && !outerScaled && Mathf.Approximately(k, 1f)) return;   // nothing to do and nothing to put back

            // What the game set since our last frame becomes the base.
            if (!known || !Same(position, held.Position)) held.BasePosition = position;
            if (!known || !Same(scale, held.Scale)) held.BaseScale = scale;

            if (!outerScaled && Mathf.Approximately(k, 1f))
            {
                piece.localPosition = held.BasePosition;
                piece.localScale = held.BaseScale;
                _held.Remove(piece);
                return;
            }

            // Its scale in its parent: its own, undone through the outer element's scale when that moves it along.
            bool follow = outerScaled && !Hides(target.OuterPart);
            Vector3 factor = Vector3.one * k;
            if (follow) factor = Vector3.Scale(factor, Divide(_held[target.Outer].BaseScale, target.Outer.localScale));
            Vector3 newScale = Vector3.Scale(held.BaseScale, factor);
            Vector3 newPosition = held.BasePosition;
            if (!target.AboutPivot && StillPoint(piece, held, target.Outer != null, out Vector3 still))
                newPosition = new Vector3(HudMath.Toward(held.BasePosition.x, still.x, factor.x),
                    HudMath.Toward(held.BasePosition.y, still.y, factor.y), HudMath.Toward(held.BasePosition.z, still.z, factor.z));
            // The outer element hidden: where it would be with the outer one at the game's size.
            if (outerScaled && !follow) AgainstOuter(target, ref newPosition, ref newScale);

            if (!Same(position, newPosition)) piece.localPosition = newPosition;
            if (!Same(scale, newScale)) piece.localScale = newScale;
            held.Position = piece.localPosition;
            held.Scale = piece.localScale;
            _held[piece] = held;
        }

        /// <summary>
        /// Turns a position and scale in the piece's parent, worked out as if the outer element were at the game's
        /// size, into the ones that give the same result with the outer element as scaled now. The outer element and
        /// what lies between are taken as unrotated, as the HUD is.
        /// </summary>
        private void AgainstOuter(Target target, ref Vector3 position, ref Vector3 scale)
        {
            Transform outer = target.Outer, parent = target.Piece.parent;
            Held o = _held[outer];
            Vector3 now = outer.localPosition, nowScale = outer.localScale;
            // The piece's parent in the outer element's own space: x_outer = a + b * x_parent (unchanged by its scale).
            Vector3 a = parent == outer ? Vector3.zero : outer.InverseTransformPoint(parent.position);
            Vector3 b = parent == outer ? Vector3.one : Divide(parent.lossyScale, outer.lossyScale);
            // Where the piece's pivot lands in the outer element's parent with the outer element at the game's size …
            Vector3 wanted = o.BasePosition + Vector3.Scale(o.BaseScale, a + Vector3.Scale(b, position));
            // … and the local position that lands there with it as scaled now.
            position = Divide(wanted - now - Vector3.Scale(nowScale, a), Vector3.Scale(nowScale, b));
            scale = Vector3.Scale(scale, Divide(o.BaseScale, nowScale));
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) =>
            new Vector3(b.x != 0f ? a.x / b.x : a.x, b.y != 0f ? a.y / b.y : a.y, b.z != 0f ? a.z / b.z : a.z);

        /// <summary>
        /// The point in the piece's parent that stays put, from its base (as the game set it), not as scaled. On the
        /// screen: on its own rectangle, at the side it's anchored to. Inside another group's element: the point of its
        /// rectangle nearest where it's attached (its anchor on the outer one), so a readout hanging below the power
        /// meter's corner keeps its top edge there and grows downward, not up into the meter.
        /// </summary>
        private static bool StillPoint(Transform piece, Held held, bool inner, out Vector3 still)
        {
            still = default;
            if (!(piece is RectTransform rect)) return false;
            Rect r = rect.rect;
            if (inner && rect.parent is RectTransform parent && rect.localRotation == Quaternion.identity)
            {
                Rect p = parent.rect;
                Vector2 anchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
                var attach = new Vector2(p.xMin + p.width * anchor.x, p.yMin + p.height * anchor.y);
                float x0 = held.BasePosition.x + held.BaseScale.x * r.xMin, x1 = held.BasePosition.x + held.BaseScale.x * r.xMax;
                float y0 = held.BasePosition.y + held.BaseScale.y * r.yMin, y1 = held.BasePosition.y + held.BaseScale.y * r.yMax;
                still = new Vector3(Mathf.Clamp(attach.x, Mathf.Min(x0, x1), Mathf.Max(x0, x1)),
                    Mathf.Clamp(attach.y, Mathf.Min(y0, y1), Mathf.Max(y0, y1)), held.BasePosition.z);
                return true;
            }
            var local = new Vector3(r.xMin + r.width * HudMath.Edge(rect.anchorMin.x, rect.anchorMax.x),
                r.yMin + r.height * HudMath.Edge(rect.anchorMin.y, rect.anchorMax.y), 0f);
            still = held.BasePosition + rect.localRotation * Vector3.Scale(held.BaseScale, local);
            return true;
        }

        private void Restore(Transform piece)
        {
            if (piece != null && _held.TryGetValue(piece, out Held held))
            {
                // Only if the game hasn't set it since: then the game's value stands.
                if (Same(piece.localPosition, held.Position)) piece.localPosition = held.BasePosition;
                if (Same(piece.localScale, held.Scale)) piece.localScale = held.BaseScale;
            }
            _held.Remove(piece);
        }

        internal void RestoreAll()
        {
            foreach (Transform piece in new List<Transform>(_held.Keys)) Restore(piece);
            _held.Clear();
        }

        private static bool Same(Vector3 a, Vector3 b) =>
            HudMath.Same(a.x, b.x) && HudMath.Same(a.y, b.y) && HudMath.Same(a.z, b.z);

        // ---- Helpers ---------------------------------------------------------------------------------------

        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();

        private static object Read(object owner, string field)
        {
            Type type = owner.GetType();
            string key = type.FullName + "." + field;
            if (!Fields.TryGetValue(key, out FieldInfo info))
            {
                info = AccessTools.Field(type, field);
                Fields[key] = info;
                if (info == null) Plugin.LogChange("hud:missing:" + key, $"HUD: {type.Name}.{field} isn't in this game version; that piece keeps the game's size.");
            }
            return info?.GetValue(owner);
        }

        private static Transform AsTransform(object value) =>
            value is Component component && component != null ? component.transform :
            value is GameObject gameObject && gameObject != null ? gameObject.transform : null;

        /// <summary>A component or GameObject field of a game component, as a transform.</summary>
        private static Transform Member(object owner, string field) => AsTransform(Read(owner, field));

        /// <summary>A list or array field of a game component, as transforms.</summary>
        private static IEnumerable<Transform> Members(object owner, string field)
        {
            if (!(Read(owner, field) is IEnumerable items)) yield break;
            foreach (object item in items)
            {
                Transform transform = AsTransform(item);
                if (transform != null) yield return transform;
            }
        }

        private static string Path(Transform transform)
        {
            var parts = new List<string>();
            for (Transform t = transform; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
