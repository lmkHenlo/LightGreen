# Changelog

All notable changes to LightGreen are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Photo filters: Mono, Mono Red, Sepia, Faded Film, Vivid, Warm, Cool, Teal & Orange, Cross Process, Duotone, Poster, Toy Camera and Dreamy, on top of the rest of the look. **F11** steps through them (**Shift+F11** goes back) and saves the choice to `Filters.Filter`; `Filters.Strength` fades them. The HUD isn't filtered.
- Your own `.cube` LUTs as photo filters: put them in `BepInEx/config/LightGreen.filters/`. The built-in filters are in `BepInEx/config/LightGreen.filters.json`, which reloads when saved.

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

[unreleased]: https://github.com/lmkHenlo/LightGreen/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/lmkHenlo/LightGreen/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/lmkHenlo/LightGreen/releases/tag/v0.1.0
