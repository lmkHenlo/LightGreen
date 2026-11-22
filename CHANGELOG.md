# Changelog

All notable changes to LightGreen are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `General.TimingLog` (off by default), for finding hitches: every 10 s, the log notes the frames that took much longer than usual, whatever caused them, and how much of the longest one was LightGreen's own work. Frames where LightGreen's own work takes over 2 ms are noted even with it off, with the time each part took.

### Fixed

- A crash (since 0.1.0's footprints): LightGreen kept the game's footprint fade to put back on prints off snow, but Unity's getter hands out a gradient that points into that one print's own memory. Once that print's memory was freed, every footprint off snow was given whatever was there; a print with 120 gradient keys (8 is the most) overran Unity's particle job and the game closed without a crash report (memory dump of 2026-11-21: 3 of 12 footprint prints with broken fades). The game's values are now copied into gradients and curves LightGreen owns, and they're only put back on prints LightGreen changed.
- A hitch every second with many `.cube` filters: checking the `LightGreen.filters` folder for changes listed every file and read its size and date on the game's thread (10–15 ms with 439 files). The folder is now checked on a background thread every 2 s.
- A 30 ms hitch when weather starts or the camera jumps (hole start, spectating): refilling the rain or snow around the new view ran the particles forward a whole fall in hundreds of small steps. They're now placed straight into the view at random ages.
- Hitching with many `.cube` filters: every `.cube` file in `LightGreen.filters/` was read into memory at start (with 439 files, about 200 MB), and the game's garbage collections grew long enough to hitch. Now a `.cube` file is read when its filter is picked, and only the one in use is kept. A `.cube` file that can't be read is reported in the log when its filter is picked, not at start.
- Stutter with weather on, and a regular hitch that grew over a long session. The game copies each player's materials on every hole and leaves the copies behind; LightGreen looked through every material in memory and kept each one it tuned, so after two hours of hosting it held about 32,000. That could keep Unity from freeing them, and it made LightGreen's look for new materials every 20 s hitch (50 to 80 ms), and made re-tuning while the weather or time of day blended take about 30 ms a frame. Now only materials on objects in the world are tuned, all of them are let go at each map change, and a blend re-tunes them at most ten times a second.
- Ball glow let go of the last balls it lit only when they glowed again: after dark ended, the destroyed ball of a player who had left stayed referenced. It now lets go of every ball and material when the glow stops and at each map change.
- A slight hitch every 10 s: looking for new materials went through every renderer in one frame (3–5 ms on a City map), and at night on City maps the search for glowing windows did the same every 15 s. Both now go through the renderers a little each frame (at most 0.5 ms), and ball glow no longer makes a new list for each ball every frame.

## [0.3.0] - 2026-11-21

### Added

- HUD sizes: a `[HUD]` section with a size for each part of the HUD, from 0 (hidden) to 100 % (the game's size): the top-left corner (progress bar, wind and stroke count), hole and ball markers, restart prompt, item bar, power meter, swing wind, angle selector, control prompts, name tags, teammate and other markers, pop-up words, chat box, kill feed, scoreboard, voice chat icons, hole banner, objective message, team score, stamina wheel, spectator prompts, quick chat wheel, crosshairs, the giant-form timer, and the game's tip pop-ups. Screen elements shrink toward the side they sit at. The hole's beam and the aim line keep the game's size.
- Cartoon rain (`Effects.CartoonRain`, off by default): bold, bright white streaks, easier to see than the rain but still rain, slanted with the wind, and a soft blue mist that keeps the colours bright. About 1,950 streaks, fewer than the rain's 2,800, with none passing right by the camera.
- Snow (`Effects.Snow`, off by default): soft, round cartoon flakes drawn in code, swaying, turning and drifting with the wind, a few big soft ones passing close to the camera, a bright, pale blue sky, blue-white fog, and slightly colder, less saturated colours. About 4,850 flakes, against the rain's 2,800. Nothing settles on the ground.
- Messages in the game's chat box: what F8–F11 or a config edit changed ("LightGreen: Filter: Noir", like a player's message) now shows as a line in your chat, seen only by you; nothing is sent. Quick presses are merged into one line per kind of change after `General.NoticeDelay` (0.5 s). With no chat box, the message shows in the top-left corner as before.
- `General.Notices` (`Chat`, `Screen` or `Off`) and `General.NoticeDelay`.
- `Filters.Filter` is a dropdown in r2modman's config editor and other config editors, listing None, the built-in filters, the ones in `LightGreen.filters.json` and your `.cube` files. The config file's list is updated when filters are added, removed or renamed, and a name that isn't in it is kept rather than reset to None.

### Changed

- **F8** (`Keys.ToggleKey`) steps through three views: LightGreen on, HUD only (the game's own look with the `[HUD]` sizes), and off (the game's own look and HUD). Its message reads `on`, `HUD only (the game's own look)` or `off (the game's own look)`.
- **F10** (`Keys.RainKey`) steps the weather: clear, rain, cartoon rain, snow, then clear again. Only one weather shows at a time: turning one on turns the others off, and if more than one is on at start, snow is kept, then cartoon rain. Its message reads `Weather: rain (1)`, `Weather: cartoon rain (1)`, `Weather: snow (1)` or `Weather: clear`.
- LightGreen's messages go to the chat box by default instead of the top-left corner; set `General.Notices = Screen` for the old behaviour.
- `Filters.Filter`'s description no longer lists the filters' names; the dropdown does.
- Rain (`Effects.Rain`) is easier to see: its streaks are a little wider and more opaque (half opaque up close, from about a third), so it reads as rain rather than haze. Its far drops no longer fall right past the camera, and it draws about 2,800 streaks, from 3,800, with the same look.

## [0.2.0] - 2026-11-19

### Added

- Photo filters, on top of the rest of the look: Mono, Mono Red, Noir, Sepia, Cyanotype, Faded Film, Instant Film, Bleach Bypass, Two-Strip, Vivid, Warm, Cool, Teal & Orange, Cross Process, Day for Night, Duotone, Poster, GameBoy, Solarize, Negative, Thermal, Night Vision, Toy Camera and Dreamy, plus Protanopia, Deuteranopia and Tritanopia to check how the game looks with colour blindness. **F11** steps through them (**Shift+F11** goes back) and saves the choice to `Filters.Filter`; `Filters.Strength` fades them. The HUD isn't filtered.
- Your own `.cube` LUTs as photo filters: put them in `BepInEx/config/LightGreen.filters/`. The built-in filters are in `BepInEx/config/LightGreen.filters.json`, which reloads when saved.

### Changed

- `LightGreen.presets.json` and `LightGreen.filters.json`: colours are written as `[r, g, b]` (the old `{"r": …}` form still reads), and `//` comments and trailing commas are allowed. A mistake is reported in the log with its line and column (or the setting, e.g. `filters[0].saturation`); while you play, the previous presets or filters stay until it's fixed, and at start the built-in ones are used.

### Fixed

- `LightGreen.presets.json` is written in full, and editing it works. Earlier versions saved only its version number there, so the built-in presets were always used and edits did nothing. On the first start, the old file is kept as `LightGreen.presets.json.v6.bak` and replaced with the full presets.

## [0.1.1] - 2026-11-18

### Added

- **F10** switches the rain off and on, coming back as heavy as it last was (or 1), and saves it to `Effects.Rain` (`Keys.RainKey`).
- `Brightness.Day` and `Brightness.Night` make days and nights brighter or darker on every hole, including at the hole's own time of day. The sun or moon, fill light, sky and haze change together; lit windows, lampposts, glowing balls and explosions keep their brightness.

### Changed

- Effects can be turned up past 2. Each has its own most, where turning it up further stops making a difference or starts to look broken, flash too hard or cost too much: 4 for most, 3 for `ColorGrading`, `WaterTuning`, `IceGlints`, `BallGlow`, `Lightning` and `Rain`, and still 2 for `TiltShift` and `Footprints`.

### Fixed

- On Snow holes at Dawn, Morning, Golden hour and Sunset, the undersides of snow and ice no longer turn yellow-green: the time of day changes how bright the fill light is, not its colour.

## [0.1.0] - 2026-11-18

The first release.

### Added

- Time of day: **F9** steps through Dawn, Morning, Noon, Golden hour, Sunset, Blue hour and Night, and back to the hole's own; the sun, sky, light, haze and water follow, blending over a few seconds (`TimeOfDay.*`).
- Colour grading and distance haze for each biome and time of day, with the colours in `LightGreen.presets.json`.
- Shadows that lean toward the biome's hue, cool in Snow and warm in Desert, with players and their cosmetics left as they are (`Effects.ShadowTint`).
- Sun highlights on water that follow the time of day, and more sparkle on snow (`Effects.WaterTuning`, `Effects.IceGlints`).
- Sharper sun shadows that reach further: at least 4096 px, out to 200 m (`Quality.SunShadowResolution`, `Quality.SunShadowDistance`).
- A lens flare toward a low sun, a miniature (tilt-shift) look for the hole overview flyover, and depth of field in photo mode; vignette and chromatic aberration, off by default.
- Ambient occlusion on every course through the game's own HTrace AO, worked out without the grass blades so open grass stays even (`Effects.AmbientOcclusion`, `Quality.AmbientOcclusionQuality`).
- `Shading.Grass` to hide the grass blades.
- Light from explosions, muzzle flashes and rocket and freeze-bomb trails, gentler with the game's Explosion flashes option off (`Effects.EffectLights`).
- Light from the Thunderstorm item's lightning strikes (`Effects.Lightning`).
- Balls glow evenly at night, your own the brightest (`Effects.BallGlow`, `Effects.BallGlowOwnOnly`).
- City holes at night: building windows and vehicle lights glow, lampposts light the street, and bright lights bloom (`Effects.CityNight`).
- Footprints in snow last longer and show as blue-grey dents (`Effects.Footprints`, `Effects.FootprintSeconds`, `Effects.FootprintMax`).
- Optional rain: streaks, splashes and a grey overcast sky, off by default (`Effects.Rain`).
- **F8** switches LightGreen off and on to compare with the game's own look (`Keys.ToggleKey`), and `General.Enabled` turns it off entirely.
- The settings file is reloaded within a second of being saved.

[unreleased]: https://github.com/lmkHenlo/LightGreen/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/lmkHenlo/LightGreen/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/lmkHenlo/LightGreen/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/lmkHenlo/LightGreen/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/lmkHenlo/LightGreen/releases/tag/v0.1.0
