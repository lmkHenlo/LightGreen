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
    /// Screen elements are found by their game components about once a second, and just before the HUD is drawn each
    /// frame (<see cref="Canvas.willRenderCanvases"/>, after the game has moved and animated it) each is scaled toward
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
    /// The hole's beam and the aim line aren't changed. Restore puts every element back as the game last set it.
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
        private float _nextScan;
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

        /// <summary>Looks again for the HUD on the next frame (a new scene).</summary>
        internal void Rescan() => _nextScan = 0f;

        private void OnWillRenderCanvases()
        {
            if (_broken) return;
            try
            {
                if (!Active)
                {
                    if (_held.Count > 0) RestoreAll();
                    return;
                }
                if (Time.unscaledTime >= _nextScan) Scan();
                foreach (Target target in _targets) Apply(target);
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

            internal Root(HudPart part, Transform transform, bool aboutPivot)
            {
                Part = part;
                Transform = transform;
                AboutPivot = aboutPivot;
            }
        }

        private void Scan()
        {
            _nextScan = Time.unscaledTime + 1f;
            List<Root> roots = FindRoots();
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
                Plugin.LogChange("hud:" + root.Part + ":" + root.Transform.GetInstanceID(),
                    $"HUD: {root.Part} is {Path(root.Transform)}.");
            if (!_layoutWritten && _layoutDueAt < 0f && GameEvents.InHole && roots.Count > 0)
                _layoutDueAt = Time.unscaledTime + 5f;
        }

        /// <summary>Each HUD group's element(s), from the game's HUD components.</summary>
        private static List<Root> FindRoots()
        {
            var roots = new List<Root>();
            void Add(HudPart part, Transform transform, bool aboutPivot = false)
            {
                if (transform != null) roots.Add(new Root(part, transform, aboutPivot));
            }
            void AddAll(HudPart part, IEnumerable<Transform> transforms, bool aboutPivot = false)
            {
                foreach (Transform transform in transforms) Add(part, transform, aboutPivot);
            }

            foreach (HoleProgressBarUi bar in Find<HoleProgressBarUi>())
            {
                Add(HudPart.ProgressBar, bar.transform);
                Add(HudPart.Strokes, Member(bar, "strokesLabel"));
            }

            // Two wind readouts: the top-left one, and the one beside the power meter, lower on the screen.
            var winds = new List<WindInfoUi>(Find<WindInfoUi>());
            winds.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            for (int i = 0; i < winds.Count; i++)
                Add(i == 0 ? HudPart.Wind : HudPart.SwingWind, winds[i].transform);

            foreach (RestartPrompt prompt in Find<RestartPrompt>()) Add(HudPart.RestartPrompt, prompt.transform);

            foreach (Hotkeys hotkeys in Find<Hotkeys>())
            {
                Add(HudPart.ItemBar, Member(hotkeys, "inventoryGroup"));
                Add(HudPart.ItemBar, Member(hotkeys, "inventoryPitchGroup"));
                Add(HudPart.ItemBar, Member(hotkeys, "cycleLeftButtonPrompt"));
                Add(HudPart.ItemBar, Member(hotkeys, "cycleRightButtonPrompt"));
                AddAll(HudPart.ItemBar, Members(hotkeys, "hotkeyUis"));
                AddAll(HudPart.ItemBar, Members(hotkeys, "golfCartSeatIcons"));
                Add(HudPart.AngleSelector, Member(hotkeys, "swingGroup"));
                Add(HudPart.AngleSelector, Member(hotkeys, "swingButtonPromptsGroup"));
                AddAll(HudPart.AngleSelector, Members(hotkeys, "swingButtonPrompts"));
            }
            // The angle arc slides between its corner and the centre: it shrinks about its own pivot.
            foreach (SwingPitchUi pitch in Find<SwingPitchUi>()) Add(HudPart.AngleSelector, pitch.transform, true);
            foreach (SwingPowerBarUi power in Find<SwingPowerBarUi>()) Add(HudPart.PowerMeter, power.transform);

            foreach (ButtonPromptManager prompts in Find<ButtonPromptManager>())
            {
                Add(HudPart.ControlPrompts, Member(prompts, "leftCorner"));
                Add(HudPart.ControlPrompts, Member(prompts, "center"));
            }

            foreach (TextChatUi chat in Find<TextChatUi>()) Add(HudPart.Chat, chat.transform);
            foreach (InfoFeed feed in Find<InfoFeed>()) Add(HudPart.KillFeed, feed.transform);
            foreach (Scoreboard board in Find<Scoreboard>()) Add(HudPart.Scoreboard, board.transform);
            foreach (VoiceChatIndicatorsHud voice in Find<VoiceChatIndicatorsHud>()) Add(HudPart.VoiceChat, voice.transform);
            foreach (HoleInfoUi banner in Find<HoleInfoUi>()) Add(HudPart.HoleBanner, banner.transform);
            foreach (GameObjectiveMessageUi objective in Find<GameObjectiveMessageUi>()) Add(HudPart.ObjectiveMessage, objective.transform);
            foreach (TeamProgressUi team in Find<TeamProgressUi>()) Add(HudPart.TeamScore, team.transform);
            foreach (SpeedBoostWheelUi wheel in Find<SpeedBoostWheelUi>()) Add(HudPart.SpeedBoost, wheel.transform);
            foreach (SpectatorButtonPrompts spectator in Find<SpectatorButtonPrompts>()) Add(HudPart.SpectatorPrompts, spectator.transform);
            foreach (QuickChatUi quickChat in Find<QuickChatUi>()) Add(HudPart.QuickChat, quickChat.transform);

            // These follow a target on screen: about their own pivot.
            foreach (Reticle reticle in Find<Reticle>()) Add(HudPart.Reticles, reticle.transform, true);
            foreach (TargetReticleUi reticle in Find<TargetReticleUi>()) Add(HudPart.Reticles, reticle.transform, true);
            foreach (LockOnTargetUi lockOn in Find<LockOnTargetUi>()) Add(HudPart.Reticles, lockOn.transform, true);
            foreach (JumboBurgerGiantFormTimerUi timer in Find<JumboBurgerGiantFormTimerUi>()) Add(HudPart.GiantTimer, timer.transform, true);
            return roots;
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
            foreach (Root root in roots) Expand(root.Part, root.Transform, root.AboutPivot, 0, claimed, result);
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

        private static T[] Find<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

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
