using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LightGreen.Core;
using LightGreen.Presets;
using LightGreen.Systems;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightGreen
{
    /// <summary>
    /// LightGreen's entry point: settings (reloaded when the config file is saved), the ToggleKey comparison with
    /// the game's own look, the TimeKey, RainKey and FilterKey and the notices they post, and the feature host, which runs each feature and switches off any that
    /// fails, putting the game back as it was. LightGreen uses no shaders of its own: everything is done with the
    /// game's shaders, URP's built-in effects and the game's own systems.
    ///
    /// Phase 1 (world look): time of day, colour grading, distance haze, lens flare, tilt-shift, photo depth of
    /// field, vignette, chromatic aberration, shadow tint, water tuning, ice glints, sharper sun shadows.
    /// Phase 2 (light): ambient occlusion (the game's HTrace AO, grass left out), grass toggle, effect lights, ball
    /// glow, lightning, City night.
    /// Phase 3 (weather): footprints in snow, and rain without the wet look.
    /// Then: snow and cartoon rain (one weather at a time, each with its sky), photo filters (colour lookup tables
    /// built at runtime, and your own .cube files), the [HUD] sizes, which ToggleKey can keep with the game's own look
    /// (HUD only), and the night sky (the moon, brighter stars, cloud under the weather, the City's glow).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.lightgreen";
        public const string Name = "LightGreen";
        public const string Version = "0.3.1";

        /// <summary>
        /// The Unity version of the game build LightGreen is made for (Super Battle Golf 1.4.1-823). A different
        /// one means the game has updated, which is when hooks and material names are most likely to change.
        /// </summary>
        internal const string ExpectedUnity = "6000.3.27f2";

        internal static ManualLogSource Log;

        private readonly FeatureHost _features = new FeatureHost();
        private readonly EnvironmentController _environment = new EnvironmentController();
        private readonly SunShadows _shadows = new SunShadows();
        private readonly AmbientOcclusion _occlusion = new AmbientOcclusion();
        private readonly LightPool _lightPool = new LightPool();
        private VolumeLayer _volumes;
        private readonly PhotoFilter _filter = new PhotoFilter();
        private Rain _rain;
        private Snow _snow;
        private CartoonRain _cartoonRain;
        private readonly Footprints _footprints = new Footprints();
        private readonly HudScaler _hud = new HudScaler();
        private Harmony _harmony;

        /// <summary>ToggleKey's steps: LightGreen on; the game's own look with the [HUD] sizes; the game's own look.</summary>
        private enum View { On, HudOnly, Off }

        private View _view = View.On;
        private float _nextConfigCheck;
        private DateTime _configWriteTime;
        private string _filterChoicesWritten;   // the filter list last written to the config file
        private int _filterRevisionSeen;
        private string _notice;
        private float _noticeUntil;
        private GUIStyle _noticeStyle;
        // Chat messages waiting out General.NoticeDelay: the latest text of each kind, in the order the kinds came.
        private readonly Dictionary<string, string> _pendingNotices = new Dictionary<string, string>();
        private readonly List<string> _pendingKinds = new List<string>();
        private float _noticesDueAt;

        private void Awake()
        {
            Log = Logger;
            CoreLog.Source = Logger;
            Settings.Bind(Config);
            _filterChoicesWritten = string.Join(", ", FilterChoices.Current());   // Bind just wrote these
            PresetStore.Load(Log);
            FilterStore.Load(Log);
            SyncFilterChoices();
            GameEvents.Hook(Log);
            _harmony = new Harmony(Guid);

            // The weather's share of the sky, then the time of day: the others take its sun, sky and colours.
            _volumes = new VolumeLayer(_environment, _filter);
            _features.Add(new WeatherSky(_environment));
            _features.Add(_environment);
            _features.Add(_filter);   // before the volumes, which take its colour table
            _features.Add(_volumes);
            _features.Add(new MaterialTuner(_environment));
            _features.Add(_shadows);
            _features.Add(_occlusion);
            _features.Add(new GrassToggle());
            // The lights: each asks the pool for this frame's lights, then the pool hands them out.
            _features.Add(new EffectLights(_lightPool));
            _features.Add(new LightningLights(_lightPool));
            _features.Add(new CityNight(_lightPool, _environment));
            _features.Add(new BallGlow(_environment));
            _features.Add(_lightPool);
            // Weather.
            _rain = new Rain(_environment);
            _features.Add(_rain);
            _cartoonRain = new CartoonRain(_environment);
            _features.Add(_cartoonRain);
            _snow = new Snow(_environment);
            _features.Add(_snow);
            _features.Add(_footprints);

            new PatchModule(typeof(EffectLights.PlayedPatch), "Hook: Effects played").TryApply(_harmony, Log);
            new PatchModule(typeof(LightningLights.StrikePatch), "Hook: Lightning strikes").TryApply(_harmony, Log);
            new PatchModule(typeof(Footprints.StepPatch), "Hook: Footsteps").TryApply(_harmony, Log);
            new PatchModule(typeof(Footprints.EffectPatch), "Hook: Footprint effects").TryApply(_harmony, Log);
            GrassToggle.Hooked = new PatchModule(typeof(GrassToggle.DrawPatch), "Hook: Grass drawing").TryApply(_harmony, Log);
            GrassFreeDepth.GrassHooked =
                new PatchModule(typeof(GrassFreeDepth.GrassPatch), "Hook: Grass material").TryApply(_harmony, Log);
            GrassFreeDepth.HTraceHooked =
                new PatchModule(typeof(GrassFreeDepth.HTracePatch), "Hook: Ambient occlusion depth").TryApply(_harmony, Log);
            new PatchModule(typeof(HudPatches.MarkerPatch), "Hook: HUD markers").TryApply(_harmony, Log);
            new PatchModule(typeof(HudPatches.NameTagPatch), "Hook: HUD name tags").TryApply(_harmony, Log);
            new PatchModule(typeof(HudPatches.PopupPatch), "Hook: HUD pop-up words").TryApply(_harmony, Log);
            _hud.Hook();

            // A new map or hole: each feature that holds the old one's things lets go of them (IMapAware).
            GameEvents.BiomeChanged += biome =>
            {
                Log.LogInfo($"Biome: {biome}.");
                ForgetLogged("hole:");
                _features.OnBiomeChanged();
                _hud.Rescan();
            };
            GameEvents.HoleChanged += () =>
            {
                _hud.Rescan();
                _features.OnHoleChanged();
            };
            GameEvents.GraphicsSettingsApplied += _shadows.OnGameApplied;
            PresetStore.Reloaded += _environment.OnPresetsReloaded;
            Settings.Time.SettingChanged += (_, __) => Notify("time", $"Time of day: {Settings.Time.Value}");
            Settings.Filter.SettingChanged += (_, __) => Notify("filter", $"Filter: {Settings.Filter.Value}");
            // One weather at a time (Weather.Start); each change says what's falling now.
            Weather.Start(notice => Notify("weather", notice));

            if (Application.unityVersion != ExpectedUnity)
                Log.LogWarning($"The game runs Unity {Application.unityVersion}; this LightGreen was made for {ExpectedUnity} " +
                               "(Super Battle Golf 1.4.1). The game may have updated: some effects may not work until LightGreen does too.");

            Settings.Enabled.SettingChanged += (_, __) =>
                Log.LogInfo(Settings.Enabled.Value ? "Enabled." : "Disabled (General.Enabled = false): the game's own look.");

            Log.LogInfo($"{Name} {Version} loaded: {Application.productName} {Application.version}, Unity {Application.unityVersion}. " +
                        $"{_features.Count} features. {Settings.ToggleKey.Value} compares with the game's own look (on, HUD only, off), " +
                        $"{Settings.TimeKey.Value} changes the time of day, {Settings.RainKey.Value} steps the weather (clear, rain, cartoon rain, snow), " +
                        $"{Settings.FilterKey.Value} changes the photo filter." +
                        (Settings.Enabled.Value ? "" : " (Off: General.Enabled = false.)"));
        }

        private static readonly int HousekeepingTiming = WorkTiming.Part("Keys and config");

        private void Update()
        {
            WorkTiming.LogLongFrames = Settings.TimingLog.Value;
            WorkTiming.NextFrame();
            long start = WorkTiming.Now;
            if (_pendingKinds.Count > 0 && Time.unscaledTime >= _noticesDueAt) FlushNotices();
            ReloadConfigIfEdited();
            PresetStore.Poll();
            FilterStore.Poll();
            if (FilterStore.Revision != _filterRevisionSeen) SyncFilterChoices();
            Vr.Poll();
            ReadKeys();
            WorkTiming.Add(HousekeepingTiming, start);

            if (Settings.Enabled.Value && _view == View.On) _features.Apply();
            else _features.Restore();
            // The [HUD] sizes are applied just before the HUD is drawn (HudScaler); this says whether they apply.
            HudScaler.Active = Settings.Enabled.Value && _view != View.Off;
        }

        private void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !Settings.Enabled.Value) return;
            if (Pressed(keyboard, Settings.ToggleKey.Value))
            {
                // On, then HUD only (the game's own look with the [HUD] sizes), then off, then on again.
                _view = _view == View.On ? View.HudOnly : _view == View.HudOnly ? View.Off : View.On;
                string state = _view == View.On ? "on" : _view == View.HudOnly ? "HUD only (the game's own look)" : "off (the game's own look)";
                Log.LogInfo(state);
                Notify("toggle", state);
            }
            if (Pressed(keyboard, Settings.TimeKey.Value) && _view == View.On)
            {
                OwnWrite(EnvironmentController.StepTime);
                Log.LogInfo($"Time of day: {Settings.Time.Value}");
            }
            if (Pressed(keyboard, Settings.RainKey.Value) && _view == View.On)
            {
                OwnWrite(Weather.Step);   // one on turns the others off (Weather.Start)
                Log.LogInfo($"{Weather.Notice()} (RainKey).");
            }
            if (Pressed(keyboard, Settings.FilterKey.Value) && _view == View.On)
            {
                bool back = keyboard.shiftKey.isPressed;
                // Setting the value saves the config file, as TimeKey does; take that write as our own too.
                OwnWrite(() => Settings.Filter.Value = PhotoFilter.Step(Settings.Filter.Value, back ? -1 : 1));
                Log.LogInfo($"Photo filter: {Settings.Filter.Value} (FilterKey{(back ? " with Shift" : "")}).");
            }
        }

        /// <summary>
        /// Makes a change that saves the config file (a key changing a setting), and takes that save as LightGreen's own,
        /// so the next check doesn't report it as an edit and reload it.
        /// </summary>
        private void OwnWrite(Action change)
        {
            change();
            _configWriteTime = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> Logged =
            new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>
        /// Logs a status line only when it says something new for its key, so switching LightGreen off and on
        /// (ToggleKey) doesn't repeat it. Keys starting "hole:" are forgotten when the biome changes.
        /// </summary>
        internal static void LogChange(string key, string message)
        {
            if (Logged.TryGetValue(key, out string last) && last == message) return;
            Logged[key] = message;
            Log.LogInfo(message);
        }

        private static void ForgetLogged(string prefix)
        {
            var keys = new System.Collections.Generic.List<string>(Logged.Keys);
            foreach (string key in keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) Logged.Remove(key);
        }

        // Keys the keyboard in use doesn't have (the Input System throws for them), found once and then skipped, so a
        // wrong key in the config costs nothing each frame. Looked at again when the keyboard changes.
        private static readonly HashSet<Key> MissingKeys = new HashSet<Key>();
        private static Keyboard _keysFor;

        private static bool Pressed(Keyboard keyboard, Key key)
        {
            if (key == Key.None) return false;
            if (keyboard != _keysFor)
            {
                _keysFor = keyboard;
                MissingKeys.Clear();
            }
            if (MissingKeys.Contains(key)) return false;
            try
            {
                return keyboard[key].wasPressedThisFrame;
            }
            catch (ArgumentException)
            {
                MissingKeys.Add(key);
                Log.LogWarning($"The key {key} isn't on this keyboard ({keyboard.displayName}); pick another in the [Keys] settings.");
                return false;
            }
        }

        /// <summary>
        /// Keeps Filters.Filter's list in the config file (the dropdown in r2modman and other config editors) in step with
        /// the filters LightGreen has loaded, saving the file when a filter is added, removed or renamed.
        /// </summary>
        private void SyncFilterChoices()
        {
            _filterRevisionSeen = FilterStore.Revision;
            string choices = string.Join(", ", FilterChoices.Current());
            if (choices == _filterChoicesWritten) return;
            _filterChoicesWritten = choices;
            try
            {
                // Take in an edit to the file first, so saving doesn't write over it.
                if (_configWriteTime != default)
                {
                    _nextConfigCheck = 0f;
                    ReloadConfigIfEdited();
                }
                OwnWrite(Config.Save);
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't update the photo filter list in the config file: {e}");
            }
        }

        /// <summary>Settings changed in the config file while playing take effect within a second.</summary>
        private void ReloadConfigIfEdited()
        {
            if (Time.unscaledTime < _nextConfigCheck) return;
            _nextConfigCheck = Time.unscaledTime + 1f;
            try
            {
                DateTime written = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                if (_configWriteTime == default)
                {
                    _configWriteTime = written;
                    return;
                }
                if (written == _configWriteTime) return;
                Config.Reload();
                // Reloading can rewrite the file (BepInEx saves the values back), which would look like
                // another edit a second later: take the time after the reload.
                _configWriteTime = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                Log.LogInfo("Config reloaded.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't reload the config: {e}");
            }
        }

        /// <summary>
        /// Says what a key or a config edit changed, where General.Notices says. For the chat, each kind of message
        /// (time, filter, weather, toggle) keeps only its latest text until no change has come for General.NoticeDelay
        /// seconds, so stepping through filters posts one line, and a time change then a filter change post two.
        /// </summary>
        private void Notify(string kind, string text)
        {
            switch (Settings.Notices.Value)
            {
                case NoticeTarget.Off:
                    return;
                case NoticeTarget.Screen:
                    ShowOnScreen(text);
                    return;
            }
            if (!_pendingNotices.ContainsKey(kind)) _pendingKinds.Add(kind);
            _pendingNotices[kind] = text;
            _noticesDueAt = Time.unscaledTime + Settings.NoticeDelay.Value;
            if (Settings.NoticeDelay.Value <= 0f) FlushNotices();
        }

        /// <summary>Posts the waiting messages to the chat; any the chat can't take go to the top-left corner.</summary>
        private void FlushNotices()
        {
            var onScreen = new List<string>();
            foreach (string kind in _pendingKinds)
            {
                string text = _pendingNotices[kind];
                // With the chat box hidden ([HUD] Chat at 0), the corner instead.
                if (HudScaler.Hides(HudPart.Chat) || !ChatNotices.TryPost(text)) onScreen.Add(text);
            }
            _pendingKinds.Clear();
            _pendingNotices.Clear();
            if (onScreen.Count > 0) ShowOnScreen(string.Join("  ·  ", onScreen));
        }

        private void ShowOnScreen(string text)
        {
            _notice = text;
            _noticeUntil = Time.unscaledTime + 2.5f;
        }

        /// <summary>A short line in the top-left corner (General.Notices = Screen, or when there is no chat box).</summary>
        private void OnGUI()
        {
            if (_notice == null || Time.unscaledTime > _noticeUntil) return;
            float scale = Screen.height / 1080f;
            if (_noticeStyle == null) _noticeStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _noticeStyle.fontSize = Mathf.RoundToInt(22 * scale);
            var rect = new Rect(24 * scale, 24 * scale, 900 * scale, 40 * scale);
            string text = "LightGreen: " + _notice;
            _noticeStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, _noticeStyle);
            _noticeStyle.normal.textColor = Color.white;
            GUI.Label(rect, text, _noticeStyle);
        }

        private void OnDestroy()
        {
            _features.RestoreAll();
            _volumes?.Destroy();
            _filter.Destroy();
            _occlusion.Destroy();
            _lightPool.Destroy();
            _rain?.Destroy();
            _snow?.Destroy();
            _cartoonRain?.Destroy();
            _hud.Unhook();
            _harmony?.UnpatchSelf();
            GameEvents.Unhook();
        }
    }
}
