# LightGreen

[![license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![standard-readme compliant](https://img.shields.io/badge/readme%20style-standard-brightgreen.svg?style=flat-square)](https://github.com/RichardLitt/standard-readme)

Lighting, atmosphere and weather for Super Battle Golf that keep its cel-shaded look. Client-side: only you need it.

LightGreen adds a time of day you can change (F9), colour grading and distance haze for each biome, lens effects, shadows that lean toward the biome's hue, sharper and longer sun shadows, ambient occlusion on every course, light from explosions, muzzle flashes, rocket trails and lightning, glowing balls at night, lit City streets at night, longer-lasting footprints in snow, and optional rain. Every effect is made with the game's own shaders, URP's built-in effects and the game's own systems, so it keeps the game's bright look and ships nothing but one plugin DLL. **F8** switches it off and on to compare with the game's own look. It only changes your own screen: nothing is sent over the network, and the host and other players don't need the mod.

The source folder is called `next` because it sits beside the earlier, unreleased version of LightGreen (`master`), which it replaces; the project, plugin and Thunderstore package are all called LightGreen.

## Table of Contents

- [Background](#background)
- [Install](#install)
- [Usage](#usage)
- [Known issues](#known-issues)
- [Building from source](#building-from-source)
- [Releasing](#releasing)
- [Files](#files)
- [Redistribution](#redistribution)
- [API](#api)
- [Maintainers](#maintainers)
- [Contributing](#contributing)
- [License](#license)

## Background

Super Battle Golf is drawn with Unity's Universal Render Pipeline (URP) and its own cel-shaded (toon) shaders. LightGreen was first built around rebuilt copies of those shaders in a Unity asset bundle, which had to be rebuilt whenever the game moved to a new Unity version. This version was started again from scratch with one rule: **no shader bundle**. Everything is done with the game's own shaders and materials, URP's built-in post-processing and lights, and the game's own systems (its HTrace ambient occlusion, its wind, its effects), so there is nothing to build in Unity.

It was built and tested in three phases, recorded in [docs/testing.md](docs/testing.md):

1. **World look:** time of day, colour grading, distance haze, lens flare, tilt-shift for the hole overview, photo depth of field, vignette, chromatic aberration, shadow tint, water and ice tuning, sharper sun shadows.
2. **Light:** ambient occlusion, a grass switch, effect lights, ball glow, lightning, City at night.
3. **Weather:** longer-lasting footprints in snow, and optional rain without wet surfaces.

Each effect is a *feature*: it is applied every frame on a hole, and put back exactly as the game had it when LightGreen is switched off, outside a hole, or if it fails. A feature that throws an error, or whose hook no longer matches the game after an update, switches itself off and the rest carry on.

### See Also

- [BepInEx](https://github.com/BepInEx/BepInEx), the mod loader
- [Harmony](https://github.com/pardeike/Harmony), the runtime patching library (BepInEx ships the HarmonyX fork)
- [CHANGELOG.md](CHANGELOG.md), the changes in each release
- [docs/testing.md](docs/testing.md), the checks and results for each phase

## Install

### Dependencies

- [BepInEx 5](https://github.com/BepInEx/BepInEx) for Windows x64. On Thunderstore this is `BepInEx-BepInExPack-5.4.2305`.
- Super Battle Golf 1.4.0 (Unity 6000.3.27f2). LightGreen logs a warning if the game runs a different Unity version.
- **Client-side only.** Only the players who want the look need the mod. It works in any lobby, whoever hosts.

### With a mod manager

Install `LightGreen` from Thunderstore with r2modman or another Thunderstore mod manager. BepInEx is installed with it as a dependency.

### By hand

Install BepInEx 5 into the game folder and start the game once. Then copy the DLL into the plugins folder:

```
Super Battle Golf/
└── BepInEx/
    └── plugins/
        └── LightGreen/
            └── LightGreen.dll
```

To build it yourself, see [Building from source](#building-from-source).

## Usage

Start a hole: LightGreen is on from the start. Press **F9** to step through the times of day (Dawn, Morning, Noon, Golden hour, Sunset, Blue hour, Night, and back to the hole's own), and **F8** to switch LightGreen off and on to compare with the game's own look.

The settings live in `BepInEx/config/com.lightgreen.cfg`, which is created on first launch; with r2modman you can also edit them under **Config editor**. The file is reloaded within a second of being saved, so changes show while you play. For example, to turn rain on and make ambient occlusion cheaper:

```ini
[Effects]
## How heavy the rain is: 0 = off, 1 = tuned, 2 = double.
Rain = 1

[Quality]
## Ambient occlusion detail: Low, Medium or High.
AmbientOcclusionQuality = Low
```

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | LightGreen on. Off: the game looks exactly as it does without the mod. |
| `Keys.ToggleKey` | `F8` | Switches LightGreen off and on while playing, to compare with the game's own look. |
| `Keys.TimeKey` | `F9` | Steps through the times of day. |
| `TimeOfDay.Time` | `HoleDefault` | `HoleDefault` keeps each hole's own sun and sky; `Dawn`, `Morning`, `Noon`, `GoldenHour`, `Sunset`, `BlueHour`, `Night` move the sun and recolour the sky, light, haze and water. Only you see it. |
| `TimeOfDay.ProgressionMinutes` | `0` | Minutes for a full day cycle; 0 keeps the time put. |
| `TimeOfDay.TransitionSeconds` | `3` | How long a change of time of day blends in. |
| `Effects.*` | `1` | Strength per effect, 0 off, 1 tuned, 2 double: `ShadowTint`, `ColorGrading`, `DistanceHaze`, `LensFlare`, `WaterTuning`, `IceGlints`, `TiltShift`, `PhotoDepthOfField`, `AmbientOcclusion`, `EffectLights`, `BallGlow`, `Lightning`, `CityNight`, `Footprints`. |
| `Effects.Vignette`, `Effects.ChromaticAberration`, `Effects.Rain` | `0` | Off unless you turn them up, on the same 0–2 scale. |
| `Effects.BallGlowOwnOnly` | `false` | Only your own ball glows. |
| `Effects.FootprintSeconds` | `45` | How long a footprint in snow lasts, 2–180 seconds (the game's last about 2). |
| `Effects.FootprintMax` | `300` | Most footprints in snow at once, 20–2000; past it the oldest fade. |
| `Shading.Grass` | `true` | `false` hides the game's grass blades (the terrain's colour shows instead). |
| `Quality.SunShadowResolution` | `4096` | Sun shadow map size (0, 2048, 4096, 8192); 0 keeps the game's. |
| `Quality.SunShadowDistance` | `200` | How far (metres) the sun casts shadows, 0–500; 0 keeps the game's. |
| `Quality.AmbientOcclusionQuality` | `Medium` | `Low` (quarter resolution), `Medium` (half), `High` (full). |

The colours for each biome and time of day are in `BepInEx/config/LightGreen.presets.json`, written on first launch and reloaded when saved.

## Known issues

- **Lights need the game's Dynamic Lighting setting** (Settings > Graphics), which is on by default. With it off, effect lights, lightning and City lampposts give no light; everything else still works.
- **Ambient occlusion shows slightly jagged edges** where surfaces meet or overlap (beam joints, between leaf clumps). It is worked out per pixel and laid over the anti-aliased image, because the game's toon shader has no variant that reads it while drawing. `Quality.AmbientOcclusionQuality = High` makes the steps finer; `Effects.AmbientOcclusion = 0` turns it off.
- **F8 clashes with MaxFOVSlider,** which uses F8 by default too. Change `Keys.ToggleKey` here or `Debug.ToggleKey` there.
- **VR (SBGVR) is not yet tested.** LightGreen turns its lens effects and ambient occlusion off and halves the ice glints while a headset is in use.
- **Settings the earlier, unreleased LightGreen had** stay in `com.lightgreen.cfg`, unused; deleting the file gives a clean one. Settings this version has removed (`Effects.FilmGrain`, `Effects.Snowfall`, `Quality.AmbientOcclusionIgnoresGrass`) are taken out of the file automatically.
- **Don't install the earlier LightGreen alongside this one:** both use the id `com.lightgreen`, and BepInEx loads only one.

## Building from source

You need PowerShell 7 (`pwsh`, which includes the C# compiler), the game, and BepInEx 5's `core` folder; the .NET SDK isn't needed. From the source folder:

```powershell
pwsh ./build.ps1 -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" -InstallTo "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
```

This writes `build/plugins/LightGreen.dll` and, with `-InstallTo`, copies it into `<plugins>/LightGreen/`. Pass `-GameDir` if the game isn't in the default Steam folder.

Add `-Package` to also write the Thunderstore package, `build/lmkHenlo-LightGreen-<version>.zip`: `manifest.json`, `README.md` and `icon.png` from `thunderstore/`, `CHANGELOG.md`, `LightGreen.dll` and `LICENSE`. It stops if `thunderstore/manifest.json` and `Plugin.cs` give different versions.

`plugin/LightGreen.csproj` is for editors.

## Releasing

1. Set the version in `plugin/src/Plugin.cs` (`Version`), `plugin/src/AssemblyInfo.cs`, `plugin/LightGreen.csproj` and `thunderstore/manifest.json`, and the "Version …, tested on …" line in `thunderstore/README.md`.
2. In `CHANGELOG.md`, rename `[Unreleased]` to the new version and the release date (`## [x.y.z] - YYYY-MM-DD`) and start a new, empty `[Unreleased]` section above it.
3. Commit, and tag the commit `vx.y.z`.
4. `pwsh ./build.ps1 -BepInExCore "<profile>\BepInEx\core" -Package`, then upload `build/lmkHenlo-LightGreen-<version>.zip` to Thunderstore (Super Battle Golf community).

## Files

- `plugin/src/Plugin.cs`: entry point, settings reload, the F8 and F9 keys.
- `plugin/src/Settings.cs`: the settings, and the removed ones taken out of the file.
- `plugin/src/Core/Features.cs`: the feature interface and host. A feature that throws is put back and switched off until the game restarts; the rest carry on.
- `plugin/src/Core/PatchModule.cs`: one Harmony patch class per feature, so a hook a game update breaks turns off only that feature.
- `plugin/src/Core/GameEvents.cs`, `Biome.cs`: which biome the hole is, and the game's events LightGreen follows.
- `plugin/src/Core/MaterialEdits.cs`: material changes that can be undone exactly.
- `plugin/src/Core/GameFlashes.cs`: the game's Explosion flashes option, which makes LightGreen's flashes gentler.
- `plugin/src/Core/GameWind.cs`: the game's wind, which rain drifts with.
- `plugin/src/Core/ToonEmissive.cs`: which toon materials can be switched to glow (City windows, balls).
- `plugin/src/Presets/`: the per-biome and per-time presets and their file.
- `plugin/src/Systems/`: one file per feature: `EnvironmentController` (time of day), `VolumeLayer` (post-processing), `MaterialTuner` (shadow tint, water, ice), `SunShadows`, `AmbientOcclusion` with `GrassFreeDepth`, `GrassToggle`, `EffectLights`, `LightningLights`, `CityNight`, `BallGlow`, `LightPool` (hands the lights out), `Rain` with `ParticleLayer`, and `Footprints`.
- `plugin/src/Vr.cs`: whether a headset is in use.
- `thunderstore/`: the Thunderstore page (`README.md`), `manifest.json` and `icon.png`.
- `docs/testing.md`: the checks and results for each phase.
- `CHANGELOG.md`: the changes in each release.
- `build.ps1`: builds, installs and packages the plugin.

## Redistribution

LightGreen is meant to be redistributed, so the repository and every release contain only LightGreen's own work:

- **Ships:** LightGreen's C# code, built into `LightGreen.dll`, and its documentation and icon.
- **Never committed or shipped:** game files, decompiled or disassembled game code, Asset Store code. Anything taken from the game for reference goes in `local/`, which git ignores.

## API

LightGreen is a plugin, not a library. Other BepInEx plugins can refer to it by its GUID:

```csharp
[BepInDependency(LightGreen.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)] // "com.lightgreen"
public class MyPlugin : BaseUnityPlugin { }
```

- `LightGreen.Plugin.Guid`, `Name` and `Version` are public constants. There is no other public API.

## Maintainers

lmkHenlo, <ps.meiisbei@gmail.com>

## Contributing

Questions, bug reports and contributions are welcome by email to <ps.meiisbei@gmail.com>. Please attach `BepInEx/LogOutput.log` when reporting a problem, and a screenshot if something looks wrong.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `feat:`, `fix:`, `perf:`, `refactor:`, `style:`, `docs:` and `chore:`, with `BREAKING CHANGE:` for config settings that are removed or renamed. [CHANGELOG.md](CHANGELOG.md) follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

If you edit this README, please follow the [standard-readme](https://github.com/RichardLitt/standard-readme) specification.

## License

[MIT](LICENSE) © 2026 lmkHenlo
