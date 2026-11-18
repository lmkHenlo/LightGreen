# LightGreen

Lighting and visual effects for Super Battle Golf, as a BepInEx 5 mod.

This is a fresh rebuild, added to one feature at a time. The earlier version (`../master`) is kept as a reference;
proven pieces are brought over from it only when the feature they belong to is added here.

## Design rule

**No shader bundle.** Every effect uses the game's own shaders, URP's built-in effects and the game's own systems,
so there is nothing to build in Unity and nothing to break when the game's Unity version changes.

## Status

**Phase 1: world look.** Time of day (F9), colour grading, distance haze, lens flare, tilt-shift for the hole
overview, depth of field in photo mode, optional vignette and chromatic aberration, shadow tint, water
tuning, ice glints and sharper, longer sun shadows. LightGreen warns if the game's Unity version isn't the one it's
made for (6000.3.27f2, Super Battle Golf 1.4.0). Flat screen first; the lens effects are off while VR runs, and VR
gets its own pass later.

**Phase 2: light.** Ambient occlusion on every course (the game's own HTrace AO, worked out without the grass
blades so open grass doesn't speckle; off in VR), a switch to hide the grass, light from explosions, muzzle flashes
and rocket trails, glowing balls at night, light from the Thunderstorm item's lightning, and City holes at night
(lit windows and vehicles, lampposts, bloom). The lights need the game's Dynamic Lighting setting on (on by
default).

**Phase 3: weather.** Longer-lasting footprints in snow, and optional rain
(streaks, splashes and a grey overcast sky, off by default; surfaces don't get wet). All three phases are tested
and complete.

## Install

1. BepInEx 5 (x64) for Super Battle Golf, for example through r2modman.
2. Build (below) with `-InstallTo` pointing at the profile's `BepInEx/plugins` folder, or copy `build/plugins/LightGreen.dll`
   into `BepInEx/plugins/LightGreen/`.

Don't install the earlier LightGreen alongside it: both use the id `com.lightgreen`, and BepInEx loads only one.

## Settings

`BepInEx/config/com.lightgreen.cfg`, reloaded within a second when the file is saved while playing.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | LightGreen on. Off: the game looks exactly as it does without the mod. |
| `Keys.ToggleKey` | `F8` | Switches LightGreen off and on while playing, to compare with the game's own look. |
| `Keys.TimeKey` | `F9` | Steps through the times of day. |
| `TimeOfDay.Time` | `HoleDefault` | `HoleDefault` keeps each hole's own sun and sky; `Dawn`, `Morning`, `Noon`, `GoldenHour`, `Sunset`, `BlueHour`, `Night` move the sun and recolour the sky, light, haze and water. Only you see it. |
| `TimeOfDay.ProgressionMinutes` | `0` | Minutes for a full day cycle; 0 keeps the time put. |
| `TimeOfDay.TransitionSeconds` | `3` | How long a change of time of day blends in. |
| `Effects.*` | `1` (lens effects `0`) | Strength per effect, 0 off, 1 tuned, 2 double: `ShadowTint`, `ColorGrading`, `DistanceHaze`, `LensFlare`, `WaterTuning`, `IceGlints`, `TiltShift`, `PhotoDepthOfField`, `Vignette`, `ChromaticAberration`, `AmbientOcclusion`, `EffectLights`, `BallGlow`, `Lightning`, `CityNight`, `Footprints`; `Rain` (default `0`). |
| `Effects.FootprintSeconds` | `45` | How long a footprint in snow lasts (the game's last about 2 s). |
| `Effects.FootprintMax` | `300` | Most footprints in snow at once; past it the oldest fade. |
| `Effects.BallGlowOwnOnly` | `false` | Only your own ball glows. |
| `Shading.Grass` | `true` | `false` hides the game's grass blades (the terrain's colour shows instead). |
| `Quality.SunShadowResolution` | `4096` | Sun shadow map size (0, 2048, 4096, 8192); 0 keeps the game's. |
| `Quality.SunShadowDistance` | `200` | How far (metres) the sun casts shadows; 0 keeps the game's. |
| `Quality.AmbientOcclusionQuality` | `Medium` | `Low` (quarter resolution), `Medium` (half), `High` (full). |

Effect lights, lightning and City night's lampposts are URP lights, which the game only draws with its
Dynamic Lighting setting on.

Per-biome and per-time colours live in `BepInEx/config/LightGreen.presets.json` (written on first run, reloaded when
saved).

Settings from the earlier version stay in the file, unused, until removed; deleting the file gives a clean one.

## Building from source

PowerShell 7, the game, and BepInEx 5's `core` folder:

```
pwsh ./build.ps1 -Project plugin -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" -InstallTo "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
```

`plugin/LightGreen.csproj` is for editors; the build script doesn't need the .NET SDK.

## Repository layout

- `plugin/src/Plugin.cs`: entry point, settings reload, the ToggleKey.
- `plugin/src/Settings.cs`: settings.
- `plugin/src/Core/Features.cs`: the feature interface and host. A feature that throws is put back and switched off
  until the game restarts; the rest carry on.
- `plugin/src/Core/PatchModule.cs`: one Harmony patch class per feature, so a hook a game update breaks turns off only
  that feature.
- `plugin/src/Core/GameEvents.cs`, `Biome.cs`: which biome the hole is, and the game's events LightGreen follows.
- `plugin/src/Core/MaterialEdits.cs`: material changes that can be undone exactly.
- `plugin/src/Presets/`: the per-biome and per-time presets and their file.
- `plugin/src/Systems/`: one file per feature (`EnvironmentController` time of day, `VolumeLayer` post-processing,
  `MaterialTuner` shadow tint, water and ice, `SunShadows`, `AmbientOcclusion` with `GrassFreeDepth`, `GrassToggle`,
  `EffectLights`, `LightningLights`, `CityNight`, `BallGlow`, `LightPool`, which hands the lights out, and the weather:
  `Rain`, `Footprints`, with `ParticleLayer` for the rain).
- `plugin/src/Core/GameFlashes.cs`: the game's Explosion flashes option, which makes LightGreen's flashes gentler.
- `plugin/src/Core/GameWind.cs`: the game's wind, which rain drifts with.
- `plugin/src/Core/ToonEmissive.cs`: which toon materials can be switched to glow (City windows, balls).
- `plugin/src/Vr.cs`: whether a headset is in use.
- `docs/testing.md`: the checks for each step.

## Licensing

LightGreen is meant to be redistributed, so the repository and every release contain only LightGreen's own work:

- **Ships:** LightGreen's C# code only.
- **Never committed or shipped:** game files, decompiled or disassembled game code, Asset Store code. Anything taken
  from the game goes in `local/`, which git ignores.
- **Written fresh:** anything that stands in for the game's own shaders is written from notes on how they behave, not
  translated from their disassembly.

## License

MIT © 2026 lmkHenlo
