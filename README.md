# LightGreen

[![license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![standard-readme compliant](https://img.shields.io/badge/readme%20style-standard-brightgreen.svg?style=flat-square)](https://github.com/RichardLitt/standard-readme)

Lighting, atmosphere and weather for Super Battle Golf that keep its cel-shaded look. Client-side: only you need it.

LightGreen adds a time of day you can change (F9), colour grading and distance haze for each biome, lens effects, shadows that lean toward the biome's hue, sharper and longer sun shadows, ambient occlusion on every course, light from explosions, muzzle flashes, rocket trails and lightning, glowing balls at night, lit City streets at night, longer-lasting footprints in snow, and optional rain you switch with F10. Every effect is made with the game's own shaders, URP's built-in effects and the game's own systems, so it keeps the game's bright look and ships nothing but one plugin DLL. **F8** switches it off and on to compare with the game's own look. It only changes your own screen: nothing is sent over the network, and the host and other players don't need the mod.

## Table of Contents

- [Background](#background)
- [Install](#install)
- [Usage](#usage)
- [Known issues](#known-issues)
- [Building from source](#building-from-source)
- [Files](#files)
- [Maintainers](#maintainers)
- [Contributing](#contributing)
- [License](#license)

## Background

Super Battle Golf is drawn with Unity's Universal Render Pipeline (URP) and its own cel-shaded (toon) shaders.

LightGreen was built and tested in three phases, recorded in [docs/testing.md](docs/testing.md):

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

Start a hole: LightGreen is on from the start. Press **F9** to step through the times of day (Dawn, Morning, Noon, Golden hour, Sunset, Blue hour, Night, and back to the hole's own), **F8** to switch LightGreen off and on to compare with the game's own look, and **F10** to switch the rain off and on.

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
| `Keys.RainKey` | `F10` | Switches the rain off and on, as heavy as it last was (or 1). Saved to `Effects.Rain`. |
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

## Building from source

You need PowerShell 7 (`pwsh`, which includes the C# compiler), the game, and BepInEx 5's `core` folder; the .NET SDK isn't needed. From the source folder:

```powershell
pwsh ./build.ps1 -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" -InstallTo "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
```

This writes `build/plugins/LightGreen.dll` and, with `-InstallTo`, copies it into `<plugins>/LightGreen/`. Pass `-GameDir` if the game isn't in the default Steam folder.

Add `-Package` to also write the Thunderstore package, `build/lmkHenlo-LightGreen-<version>.zip`: `manifest.json`, `README.md` and `icon.png` from `thunderstore/`, `CHANGELOG.md`, `LightGreen.dll` and `LICENSE`. It stops if `thunderstore/manifest.json` and `Plugin.cs` give different versions.

`plugin/LightGreen.csproj` is for editors.

## Files

| File | What it does |
|---|---|
| `build.ps1` | Builds, installs and packages the plugin. |
| `CHANGELOG.md` | The changes in each release. |
| `docs/testing.md` | The checks and results for each phase. |
| `thunderstore/` | The Thunderstore page (`README.md`), `manifest.json` and `icon.png`. |
| `plugin/LightGreen.csproj` | Project file for editors; the build script doesn't need it. |
| `plugin/src/Plugin.cs` | Entry point: settings reload and the F8, F9 and F10 keys. |
| `plugin/src/Settings.cs` | The settings, and removed ones taken out of the file. |
| `plugin/src/Vr.cs` | Whether a headset is in use. |
| `plugin/src/Core/Features.cs` | The feature interface and host. A feature that throws is put back and switched off until the game restarts; the rest carry on. |
| `plugin/src/Core/PatchModule.cs` | One Harmony patch class per feature, so a hook a game update breaks turns off only that feature. |
| `plugin/src/Core/GameEvents.cs`, `Biome.cs` | Which biome the hole is, and the game's events LightGreen follows. |
| `plugin/src/Core/MaterialEdits.cs` | Material changes that can be undone exactly. |
| `plugin/src/Core/GameFlashes.cs` | The game's Explosion flashes option, which makes LightGreen's flashes gentler. |
| `plugin/src/Core/GameWind.cs` | The game's wind, which rain drifts with. |
| `plugin/src/Core/ToonEmissive.cs` | Which toon materials can be switched to glow (City windows, balls). |
| `plugin/src/Presets/` | The per-biome and per-time presets and their file. |
| `plugin/src/Systems/EnvironmentController.cs` | Time of day. |
| `plugin/src/Systems/VolumeLayer.cs` | Post-processing: colour grading, haze, lens effects, City night bloom. |
| `plugin/src/Systems/MaterialTuner.cs` | Shadow tint, water and ice. |
| `plugin/src/Systems/SunShadows.cs` | Sharper, longer sun shadows. |
| `plugin/src/Systems/AmbientOcclusion.cs`, `GrassFreeDepth.cs` | Ambient occlusion, worked out without the grass blades. |
| `plugin/src/Systems/GrassToggle.cs` | Hides the grass blades. |
| `plugin/src/Systems/EffectLights.cs` | Light from explosions, muzzle flashes and trails. |
| `plugin/src/Systems/LightningLights.cs` | Light from lightning strikes. |
| `plugin/src/Systems/CityNight.cs` | City windows, vehicle lights and lampposts at night. |
| `plugin/src/Systems/BallGlow.cs` | Glowing balls at night. |
| `plugin/src/Systems/LightPool.cs` | Hands the lights out each frame. |
| `plugin/src/Systems/Rain.cs`, `ParticleLayer.cs` | Rain and its particles. |
| `plugin/src/Systems/Footprints.cs` | Longer-lasting footprints in snow. |

## Maintainers

lmkHenlo, <ps.meiisbei@gmail.com>

## Contributing

Questions, bug reports and contributions are welcome by email to <ps.meiisbei@gmail.com>. Please attach `BepInEx/LogOutput.log` when reporting a problem, and a screenshot if something looks wrong.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `feat:`, `fix:`, `perf:`, `refactor:`, `style:`, `docs:` and `chore:`, with `BREAKING CHANGE:` for config settings that are removed or renamed. [CHANGELOG.md](CHANGELOG.md) follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

If you edit this README, please follow the [standard-readme](https://github.com/RichardLitt/standard-readme) specification.

## License

[MIT](LICENSE) © 2026 lmkHenlo
