using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
    /// A group can sit inside another (the stroke count inside the progress bar, say). The outer one is then scaled
    /// piece by piece round the inner one, about the outer one's still point, so each keeps its own size. A container
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

        /// <summary>One piece to scale, the group it belongs to, and the element whose still point it shrinks toward.</summary>
        private struct Target
        {
            internal Transform Piece;
            internal HudPart Part;
            internal Transform Frame;   // the group's element; the piece itself unless the group is split round another
            internal bool AboutPivot;   // shrinks about its own pivot (it follows something on screen)
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
        private bool _layoutWritten;
        private float _layoutDueAt = -1f;

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

        private void OnWillRenderCanvases()
        {
            if (_broken) return;
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
                        Apply(new Target { Piece = child, Part = container.Key, Frame = child, AboutPivot = true });
                }
                // Something was destroyed: look again, at most every two seconds.
                if (gone && _scansLeft == 0 && Time.unscaledTime >= _lastScan + 2f) _nextScan = 0f;
                if (!_layoutWritten && _layoutDueAt > 0f && Time.unscaledTime >= _layoutDueAt) WriteLayout();
            }
            catch (Exception e)
            {
                _broken = true;
                Plugin.Log.LogError($"[FAILED] HUD sizes: {e}\nThe HUD is back at the game's size until the game restarts.");
                try { RestoreAll(); } catch { /* nothing more to do */ }
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
            foreach (Root root in roots) claimed.Add(root.Transform);
            foreach (Root root in roots) Split(root, root.Transform, claimed, targets);

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
            if (!_layoutWritten && _layoutDueAt < 0f && GameEvents.InHole && roots.Count > 0)
                _layoutDueAt = Time.unscaledTime + 5f;
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
            foreach (Transform canvas in HudCanvases())
                foreach (MonoBehaviour behaviour in canvas.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    switch (behaviour)
                    {
                        case HoleProgressBarUi bar:
                            Add(HudPart.ProgressBar, bar.transform);
                            Add(HudPart.Strokes, Member(bar, "strokesLabel"));
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
                        case JumboBurgerGiantFormTimerManager timers:
                            containers.Add(new KeyValuePair<HudPart, Transform>(HudPart.GiantTimer, timers.transform));
                            break;
                    }
                }

            // Two wind readouts: the top-left one, and the one beside the power meter, lower on the screen.
            winds.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            for (int i = 0; i < winds.Count; i++)
                Add(i == 0 ? HudPart.Wind : HudPart.SwingWind, winds[i].transform);
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

        /// <summary>
        /// The pieces to scale for one element: itself, or, when other groups' elements sit inside it, its other
        /// children round them (down to them), all about the element's own still point.
        /// </summary>
        private static void Split(Root root, Transform transform, HashSet<Transform> claimed, List<Target> targets)
        {
            if (!HoldsAnother(transform, root.Transform, claimed))
            {
                targets.Add(new Target { Piece = transform, Part = root.Part, Frame = root.Transform, AboutPivot = root.AboutPivot });
                return;
            }
            foreach (Transform child in transform)
                if (!claimed.Contains(child)) Split(root, child, claimed, targets);
        }

        /// <summary>Whether another group's element is inside this transform (or is it, below the group's own element).</summary>
        private static bool HoldsAnother(Transform transform, Transform own, HashSet<Transform> claimed)
        {
            foreach (Transform other in claimed)
                if (other != null && other != own && other != transform && other.IsChildOf(transform)) return true;
            return false;
        }

        // ---- Scaling ---------------------------------------------------------------------------------------

        private void Apply(Target target)
        {
            Transform piece = target.Piece;
            if (piece == null) return;
            float k = Scale(target.Part);

            Vector3 position = piece.localPosition, scale = piece.localScale;
            bool known = _held.TryGetValue(piece, out Held held);
            if (!known && Mathf.Approximately(k, 1f)) return;   // nothing to do and nothing to put back

            // What the game set since our last frame becomes the base.
            if (!known || !Same(position, held.Position)) held.BasePosition = position;
            if (!known || !Same(scale, held.Scale)) held.BaseScale = scale;

            if (Mathf.Approximately(k, 1f))
            {
                piece.localPosition = held.BasePosition;
                piece.localScale = held.BaseScale;
                _held.Remove(piece);
                return;
            }

            Vector3 newScale = held.BaseScale * k;
            Vector3 newPosition = held.BasePosition;
            if (!target.AboutPivot && StillPoint(target, held, out Vector3 still))
                newPosition = new Vector3(HudMath.Toward(held.BasePosition.x, still.x, k),
                    HudMath.Toward(held.BasePosition.y, still.y, k), HudMath.Toward(held.BasePosition.z, still.z, k));
            if (!Same(position, newPosition)) piece.localPosition = newPosition;
            if (!Same(scale, newScale)) piece.localScale = newScale;
            held.Position = piece.localPosition;
            held.Scale = piece.localScale;
            _held[piece] = held;
        }

        /// <summary>
        /// The point in the piece's parent that stays put: on the group element's own rectangle, at the side it's
        /// anchored to.
        /// </summary>
        private static bool StillPoint(Target target, Held held, out Vector3 still)
        {
            still = default;
            if (!(target.Frame is RectTransform frame)) return false;
            Rect rect = frame.rect;
            var local = new Vector3(rect.xMin + rect.width * HudMath.Edge(frame.anchorMin.x, frame.anchorMax.x),
                rect.yMin + rect.height * HudMath.Edge(frame.anchorMin.y, frame.anchorMax.y), 0f);
            if (target.Piece == target.Frame)
            {
                // The element itself, from its own base (as the game set it), not as scaled.
                still = held.BasePosition + frame.localRotation * Vector3.Scale(held.BaseScale, local);
                return true;
            }
            // A piece of a split element: the element isn't scaled, so its point in the world is where it is.
            Transform parent = target.Piece.parent;
            if (parent == null) return false;
            still = parent.InverseTransformPoint(frame.TransformPoint(local));
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

        // ---- The layout file -------------------------------------------------------------------------------

        /// <summary>
        /// Once a game, a few seconds into the first hole: every HUD group's elements and the pieces scaled, with
        /// their anchors, pivots and sizes, then the HUD's whole tree, to BepInEx/LightGreen.hud.txt. For checking
        /// which element is which.
        /// </summary>
        private void WriteLayout()
        {
            _layoutWritten = true;
            try
            {
                var text = new StringBuilder();
                text.AppendLine($"LightGreen {Plugin.Version}: the HUD as found, {DateTime.Now:yyyy-MM-dd HH:mm}, {Screen.width}x{Screen.height}.");
                text.AppendLine();
                text.AppendLine("== Groups and the pieces scaled (piece <- group element)");
                var canvases = new HashSet<Transform>();
                foreach (Target target in _targets)
                {
                    if (target.Piece == null) continue;
                    text.Append(target.Part).Append(": ").Append(Describe(target.Piece));
                    if (target.Frame != target.Piece) text.Append("  <- ").Append(Path(target.Frame));
                    if (target.AboutPivot) text.Append("  (about its pivot)");
                    text.AppendLine();
                    Canvas canvas = target.Piece.GetComponentInParent<Canvas>(true);
                    if (canvas != null) canvases.Add(canvas.rootCanvas.transform);
                }
                foreach (Transform canvas in canvases)
                {
                    text.AppendLine();
                    text.AppendLine("== Tree of " + Path(canvas));
                    Tree(text, canvas, 0);
                }
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "LightGreen.hud.txt");
                System.IO.File.WriteAllText(path, text.ToString());
                Plugin.Log.LogInfo($"HUD layout written to {path}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't write the HUD layout: {e.Message}");
            }
        }

        private static void Tree(StringBuilder text, Transform transform, int depth)
        {
            if (depth > 9) return;
            text.Append(' ', depth * 2).Append(transform.name);
            if (!transform.gameObject.activeSelf) text.Append(" [off]");
            var names = new List<string>();
            foreach (Component component in transform.GetComponents<Component>())
                if (component != null && !(component is Transform)) names.Add(component.GetType().Name);
            if (names.Count > 0) text.Append(" {").Append(string.Join(", ", names)).Append('}');
            if (transform is RectTransform rect)
                text.Append($" a({rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##})-({rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##})" +
                            $" p({rect.pivot.x:0.##},{rect.pivot.y:0.##}) {rect.rect.width:0}x{rect.rect.height:0}");
            text.AppendLine();
            foreach (Transform child in transform) Tree(text, child, depth + 1);
        }

        private static string Describe(Transform transform)
        {
            string text = Path(transform) + (transform.gameObject.activeInHierarchy ? "" : " [off]");
            if (transform is RectTransform rect)
                text += $" a({rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##})-({rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##})" +
                        $" p({rect.pivot.x:0.##},{rect.pivot.y:0.##}) {rect.rect.width:0}x{rect.rect.height:0}" +
                        $" scale {transform.localScale.x:0.###}";
            return text;
        }
    }
}
