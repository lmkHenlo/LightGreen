# LightGreen

[![license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![standard-readme compliant](https://img.shields.io/badge/readme%20style-standard-brightgreen.svg?style=flat-square)](https://github.com/RichardLitt/standard-readme)

Lighting, atmosphere, weather and photo filters for Super Battle Golf that keep its cel-shaded look.

LightGreen adds a time of day you can change (F9), colour grading and distance haze for each biome, lens effects, shadows that lean toward the biome's hue, sharper and longer sun shadows, ambient occlusion on every course, light from explosions, muzzle flashes, rocket trails and lightning, glowing balls at night, lit City streets at night, a moon and brighter stars at night, longer-lasting footprints in snow, optional rain, cartoon rain or cartoon snow you step through with F10, photo filters you step through with F11, your own .cube LUTs included, and a size for each part of the HUD, from 0 (hidden) to 100 %. Every effect is made with the game's own shaders, URP's built-in effects and the game's own systems, so it keeps the game's bright look and ships nothing but one plugin DLL. **F8** steps between LightGreen on, HUD only (the game's own look with your HUD sizes) and off, to compare with the game's own look. It only changes your own screen: nothing is sent over the network, and the host and other players don't need the mod.

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

Later releases (the rain key, day and night brightness, photo filters, chat notices, snow, cartoon rain, HUD sizes and the night sky) were tested the same way, in the same file.

Each effect is a *feature*: it is applied every frame on a hole, and put back exactly as the game had it when LightGreen is switched off, outside a hole, or if it fails. A feature that throws an error, or whose hook no longer matches the game after an update, switches itself off and the rest carry on.

### See Also

- [BepInEx](https://github.com/BepInEx/BepInEx), the mod loader
- [Harmony](https://github.com/pardeike/Harmony), the runtime patching library (BepInEx ships the HarmonyX fork)
- [CHANGELOG.md](CHANGELOG.md), the changes in each release
- [docs/testing.md](docs/testing.md), the checks and results for each phase

## Install

### Dependencies

- [BepInEx 5](https://github.com/BepInEx/BepInEx) for Windows x64. On Thunderstore this is `BepInEx-BepInExPack-5.4.2305`.
- Super Battle Golf 1.4.1-823 (Unity 6000.3.27f2). LightGreen logs a warning if the game runs a different Unity version.
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

Start a hole: LightGreen is on from the start. Press **F9** to step through the times of day (Dawn, Morning, Noon, Golden hour, Sunset, Blue hour, Night, and back to the hole's own), **F8** to step between LightGreen on, HUD only and off to compare with the game's own look, **F10** to step the weather (clear, rain, cartoon rain, snow), and **F11** to step through the photo filters (**Shift+F11** goes back). Each key says what it changed with a line in the game's chat box ("LightGreen: Filter: Noir", like a player's message), seen only by you; `General.Notices` moves it to the top-left corner or turns it off.

The settings live in `BepInEx/config/com.lightgreen.cfg`, which is created on first launch; with r2modman you can also edit them under **Config editor**. The file is reloaded within a second of being saved, so changes show while you play. For example, to turn rain on and make ambient occlusion cheaper:

```ini
[Effects]
Rain = 1

[Quality]
AmbientOcclusionQuality = Low
```

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | LightGreen on. Off: the game looks exactly as it does without the mod. |
| `General.Notices` | `Chat` | Where LightGreen says what a key or a config edit changed. `Chat`: a line in the game's chat box, seen only by you (nothing is sent; with no chat box, such as in some menus, the top-left corner). `Screen`: the top-left corner. `Off`: nowhere. |
| `General.NoticeDelay` | `0.5` | Chat only: seconds (0–2) to wait after the last change before posting, so stepping through filters posts one line, not one per press. A time change then a filter change still post two lines. `0` posts each change at once. |
| `General.TimingLog` | `false` | For finding hitches: every 10 s, logs the frames that took much longer than usual (whatever the cause) and how much of each was LightGreen's own work, the garbage collections (and whether one fell in that frame) and the managed heap's size. Frames where LightGreen's work takes over 2 ms are logged even when this is off. |
| `Keys.ToggleKey` | `F8` | Steps LightGreen while playing: on, HUD only (the game's own look with the `[HUD]` sizes), off (the game's own look and HUD). |
| `Keys.TimeKey` | `F9` | Steps through the times of day. |
| `Keys.RainKey` | `F10` | Steps the weather: clear, rain, cartoon rain, snow, then clear again. Each comes back as heavy as it last was (or 1). Saved to `Effects.Rain`, `Effects.CartoonRain` and `Effects.Snow`. |
| `Keys.FilterKey` | `F11` | Steps to the next photo filter (with Shift, the previous one). Saved to `Filters.Filter`. |
| `TimeOfDay.Time` | `HoleDefault` | `HoleDefault` keeps each hole's own sun and sky; `Dawn`, `Morning`, `Noon`, `GoldenHour`, `Sunset`, `BlueHour`, `Night` move the sun and recolour the sky, light, haze and water. Only you see it. |
| `TimeOfDay.ProgressionMinutes` | `0` | Minutes for a full day cycle; 0 keeps the time put. |
| `TimeOfDay.TransitionSeconds` | `3` | How long a change of time of day blends in. |
| `Brightness.Day` | `1` | How bright daylight is, 0.5–1.5: the sun, fill light, sky and haze together, on every hole and at every time of day. |
| `Brightness.Night` | `1` | How bright night is, 0.25–1.5 (lower = darker); dusk falls between the two. Lit windows, lampposts, glowing balls and explosions keep their brightness. |
| `Filters.Filter` | `None` | The photo filter (below), or the name of one of your `.cube` files. r2modman's config editor shows it as a dropdown of every filter LightGreen has found, sorted A–Z (the config file lists them in F11 order) and kept up to date as filters are added or removed. A `.cube` file whose name has a comma in it shows up split in two there, but F11 still reaches it. |
| `Filters.Strength` | `1` | How much of the photo filter shows, 0–1, its vignette, fringes, distortion and glow included. In between, a filter is mixed with the normal picture, so Negative and Solarize turn grey around 0.5. |
| `Effects.*` | `1` | Strength per effect, 0 off, 1 tuned, 2 double, each up to its own most (below): `ShadowTint`, `ColorGrading`, `DistanceHaze`, `LensFlare`, `WaterTuning`, `IceGlints`, `TiltShift`, `PhotoDepthOfField`, `AmbientOcclusion`, `EffectLights`, `BallGlow`, `Lightning`, `CityNight`, `Footprints`. |
| `Effects.Vignette`, `Effects.ChromaticAberration`, `Effects.Rain`, `Effects.CartoonRain`, `Effects.Snow` | `0` | Off unless you turn them up, on the same scale. |
| `Effects.Stars` | `2` | How bright the stars are at night, on the same scale (1 is the sky's own level, up to 4): higher also brings out fainter stars. They come out late at dusk and hide behind rain, snow and cartoon rain's cloud. |
| `Effects.Rain` | `0` | Rain on every course: streaks, splashes where it lands and a grey overcast sky with thicker cloud; surfaces don't get wet. 0–3, how heavy. One weather at a time, as for snow. |
| `Effects.CartoonRain` | `0` | Cartoon rain on every course: bold, bright white streaks, easier to see than `Effects.Rain` but still rain, and a soft blue mist that keeps the colours bright. 0–3, how heavy. One weather at a time, as for snow. |
| `Effects.Snow` | `0` | Cartoon snow on every course: soft, round flakes drifting with the wind, a few big ones close by, a bright, pale blue sky and blue-white fog, and slightly colder colours. Nothing settles on the ground. 0–3, how heavy. Only one weather shows at a time: turning one on turns the others off, and if more than one is on when the game starts, snow is kept, then cartoon rain. |
| `Effects.BallGlowOwnOnly` | `false` | Only your own ball glows. |
| `Effects.FootprintSeconds` | `45` | How long a footprint in snow lasts, 2–180 seconds (the game's last about 2). |
| `Effects.FootprintMax` | `300` | Most footprints in snow at once, 20–2000; past it the oldest fade. |
| `Shading.Grass` | `true` | `false` hides the game's grass blades (the terrain's colour shows instead). |
| `Quality.SunShadowResolution` | `4096` | Sun shadow map size (0, 2048, 4096, 8192); 0 keeps the game's. Never lower than the size the game's own Shadow quality option sets: shadows only get sharper. |
| `Quality.SunShadowDistance` | `200` | How far (metres) the sun casts shadows, 0–500; 0 keeps the game's. Never shorter than the game's own. |
| `Quality.AmbientOcclusionQuality` | `Medium` | `Low` (quarter resolution), `Medium` (half), `High` (full). |

Each effect goes up to its own most: where turning it up further stops making a difference, or where it starts to look broken, flash too hard or cost too much. A higher value in the config is lowered to the most.

| Up to | Effects |
|---|---|
| 4 | `ShadowTint`, `DistanceHaze`, `LensFlare`, `PhotoDepthOfField`, `Vignette`, `ChromaticAberration`, `AmbientOcclusion`, `EffectLights`, `CityNight`, `Stars` |
| 3 | `ColorGrading`, `WaterTuning`, `IceGlints`, `BallGlow`, `Lightning`, `Rain`, `CartoonRain`, `Snow` |
| 2 | `TiltShift`, `Footprints` |

The colours for each biome and time of day are in `BepInEx/config/LightGreen.presets.json`, written on first launch and reloaded when saved. A mistake in it is reported in `BepInEx/LogOutput.log` with its line and column, and the previous colours stay until it's fixed. A setting name it doesn't recognise, such as a misspelling, is ignored, and that setting keeps its default. The same goes for `LightGreen.filters.json`. Either file over 4 MB is refused, with its size in the log.

### HUD sizes

Each part of the HUD has its own size in the `[HUD]` section, in percent: 100 (the default) is the game's size, 50 is half, 0 hides it. Screen elements shrink toward the side they sit at, so the item bar keeps its bottom-right corner and the progress bar its top-left. Changes show at once. Only you see them. The hole's beam and the aim line can't be changed.

| Setting | What it sizes |
|---|---|
| `ProgressBar` | The top-left corner as one: the hole progress bar (bar, flag, players' portraits), the wind readout and the stroke count |
| `HoleMarker` | The marker over the hole, with its distance and its arrow at the screen's edge |
| `BallMarker` | The marker over your ball, with its distance and arrow |
| `RestartPrompt` | "Hold R to restart" |
| `ItemBar` | The item bar, bottom right: the slots, the item's name and the angle badge |
| `PowerMeter` | The swing power meter |
| `SwingWind` | The wind readout beside the power meter while you swing |
| `AngleSelector` | The swing angle, bottom right while you swing, and the angle arc shown while you change it |
| `ControlPrompts` | Control prompts such as "Adjust Angle" |
| `NameTags` | The names over other players |
| `TeammateMarkers` | The team icons over your teammates |
| `OtherMarkers` | Objective, ball dispenser and homing-missile warning icons |
| `PopupText` | The words that pop up over players |
| `Chat` | The chat box (at 0, LightGreen's own messages show in the top-left corner instead) |
| `KillFeed` | The game's event messages: knockouts, chip-ins, players finishing |
| `Scoreboard` | The scoreboard (Tab) |
| `VoiceChat` | The icons of players speaking on voice chat |
| `HoleBanner` | The hole's name, number and par at the start of a hole |
| `ObjectiveMessage` | The title at the start of a match |
| `TeamScore` | The team scores and pips in team modes |
| `SpeedBoost` | The stamina wheel while sprinting |
| `SpectatorPrompts` | The previous and next prompts while spectating |
| `QuickChat` | The quick chat wheel |
| `Reticles` | Item crosshairs, the interact marker and lock-on markers |
| `GiantTimer` | The Jumbo Burger giant-form countdown |
| `Hints` | The game's tip pop-ups ("Cancel Swing") and the tutorial's objective box |

**F8** steps through three views: LightGreen on, then HUD only (the game's own look with your HUD sizes), then off (the game's own look and HUD).

### Photo filters

A photo filter changes the finished picture's colours like a photo editor, on top of the time of day and biome look. The HUD isn't filtered. None of the filters use film grain.

F11 goes through them in this order. The last three show roughly how the world looks with colour blindness; the HUD isn't filtered, so it isn't included.

| Filter | Look |
|---|---|
| Mono | Neutral black and white |
| Mono Red | Black and white through a red filter: darker skies and water, brighter skin, wood and sand |
| Noir | Hard black and white: deep shadows, bright highlights, dark corners |
| Sepia | Warm brown monochrome |
| Cyanotype | Prussian blue and white, like a blueprint |
| Faded Film | Lifted blacks, softer contrast, slightly warm |
| Instant Film | Lifted blacks, soft highlights, a slight green-yellow cast |
| Bleach Bypass | Silvery, little colour, harsh contrast |
| Two-Strip | Early two-colour film: reds and cyans only |
| Vivid | More contrast and saturation |
| Warm | Golden, sunlit |
| Cool | Blue-teal, crisp |
| Teal & Orange | Teal shadows, warm highlights |
| Cross Process | Yellow-green highlights, blue shadows |
| Day for Night | Daylight graded to look like moonlight: darker, bluer, little colour |
| Duotone | Navy to peach |
| Poster | Fewer colour steps, like a poster or comic |
| GameBoy | Four shades of green, like an old handheld console's screen |
| Solarize | The darkroom effect: the brightest parts turn dark again |
| Negative | Every colour inverted |
| Thermal | False colour from dark blue to white, by brightness (not heat) |
| Night Vision | Green, with glowing highlights and dark corners |
| Toy Camera | Saturated cross-process, dark corners, a bulging lens |
| Dreamy | Pastel and soft, with a glow around bright parts |
| Protanopia | Roughly how the game looks without red-sensitive cones |
| Deuteranopia | Roughly how it looks without green-sensitive cones |
| Tritanopia | Roughly how it looks without blue-sensitive cones |

The filters are in `BepInEx/config/LightGreen.filters.json`, written on first launch and reloaded when saved: each sets any of a `.cube` file, a channel mixer, warmth and tint, brightness, contrast, saturation, tone curves, split toning, a gradient map, fade and posterize, plus a vignette, lens distortion, colour fringes and bloom. To use your own LUTs, put `.cube` files (3D or 1D, as Lightroom, Photoshop and Resolve export them) in `BepInEx/config/LightGreen.filters/`. Each one becomes a filter named after the file. A `.cube` file is read when its filter is picked, in the background, so the game doesn't stop while it's read: the filter before stays on screen for a frame or two until the new one is ready. Only the one in use is kept in memory, so a folder of hundreds costs nothing until used. One that can't be read, or is over 96 MB, is reported in the log when picked. In VR, filters keep their colours but not their vignette, distortion or fringes.

## Known issues

- **Lights need the game's Dynamic Lighting setting** (Settings > Graphics), which is on by default. With it off, effect lights, lightning and City lampposts give no light; everything else still works.

## Building from source

You need PowerShell 7 (`pwsh`, which includes the C# compiler), the game, and BepInEx 5's `core` folder; the .NET SDK isn't needed. From the source folder:

```powershell
pwsh ./build.ps1 -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" -InstallTo "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
```

This writes `build/plugins/LightGreen.dll` and, with `-InstallTo`, copies it into `<plugins>/LightGreen/`. Pass `-GameDir` if the game isn't in the default Steam folder.

Add `-Package` to also write the Thunderstore package, `build/lmkHenlo-LightGreen-<version>.zip`: `manifest.json`, `README.md` and `icon.png` from `thunderstore/`, `CHANGELOG.md`, `LightGreen.dll` and `LICENSE`. It stops if `thunderstore/manifest.json` and `Plugin.cs` give different versions.

Add `-Test` to also run the offline checks in `tests/`: the `.cube` reader, the filter maths, the presets and filters files and the built-in filters, the preset lookup, LightGreen's file handling (size limits, whole-file writes), the HUD maths, the weather art and pixels, LightGreen's random numbers and the material copy lookup. They run in a PowerShell of their own, with no game running, and the build stops if one fails. What needs the game is checked by hand, in `docs/testing.md`.

`plugin/LightGreen.csproj` is for editors.

## Files

| File | What it does |
|---|---|
| `build.ps1` | Builds, installs, tests and packages the plugin. |
| `CHANGELOG.md` | The changes in each release. |
| `docs/testing.md` | The checks and results for each phase. |
| `tests/Tests.cs` | The offline checks `build.ps1 -Test` runs. |
| `thunderstore/` | The Thunderstore page (`README.md`), `manifest.json` and `icon.png`. |
| `plugin/LightGreen.csproj` | Project file for editors; the build script doesn't need it. |
| `plugin/src/Plugin.cs` | Entry point: settings reload, the F8, F9, F10 and F11 keys, and their messages. |
| `plugin/src/AssemblyInfo.cs` | The DLL's title, description and version (kept equal to `Plugin.Version`). |
| `plugin/src/Settings.cs` | The settings, and removed ones taken out of the file. |
| `plugin/src/Vr.cs` | Whether a headset is in use. |
| `plugin/src/Core/Features.cs` | The feature interface and host. A feature that throws is put back and switched off until the game restarts; the rest carry on. |
| `plugin/src/Core/ChatNotices.cs` | Posts LightGreen's messages to the game's chat box, on your screen only. |
| `plugin/src/Core/HudMath.cs` | The arithmetic of HUD sizes: percent to scale, and which side an element shrinks toward. |
| `plugin/src/Core/PatchModule.cs` | One Harmony patch class per feature, so a hook a game update breaks turns off only that feature. |
| `plugin/src/Core/GameEvents.cs`, `Biome.cs` | Which biome the hole is, and the game's events LightGreen follows. |
| `plugin/src/Core/MaterialEdits.cs` | Material changes that can be undone exactly. |
| `plugin/src/Core/GameFlashes.cs` | The game's Explosion flashes option, which makes LightGreen's flashes gentler. |
| `plugin/src/Core/GameWind.cs` | The game's wind, which the weather drifts with. |
| `plugin/src/Core/FlakeArt.cs`, `RainArt.cs` | Draw the snowflakes, and cartoon rain's streaks, in code. |
| `plugin/src/Core/ToonEmissive.cs` | Which toon materials can be switched to glow (City windows, balls). |
| `plugin/src/Core/LutBuilder.cs`, `CubeLut.cs` | A photo filter's colours as a lookup table, and the `.cube` file reader. |
| `plugin/src/Core/Json.cs` | Reads and writes the presets and filters files (at most 64 levels deep). |
| `plugin/src/Core/SafeFile.cs` | LightGreen's file reads, with a size limit, and writes that replace a file whole or not at all. |
| `plugin/src/Core/PatchGuard.cs` | Keeps a Harmony patch from breaking the game's own method: the first failure is logged and the patch stands down. |
| `plugin/src/Core/CoreLog.cs` | The log the Core classes write to, and failures said once. |
| `plugin/src/Core/TextureArt.cs` | Turns pictures drawn in code into textures. |
| `plugin/src/Core/ObjectSweep.cs` | Goes through every renderer (or other object) in the world a few frames at a time, so a search doesn't hitch. |
| `plugin/src/Core/ParticleValues.cs` | Copies of the game's particle curves and gradients that LightGreen owns. |
| `plugin/src/Core/LocalRandom.cs` | LightGreen's own random numbers, so the game's sequence is left alone. |
| `plugin/src/Core/WorkTiming.cs` | Times LightGreen's work each frame and logs the frames that took long. |
| `plugin/src/Presets/` | The per-biome and per-time presets, the photo filters, their files (`JsonFile`: read, written, backed up and watched), and the list of filters for `Filters.Filter`. |
| `plugin/src/Systems/EnvironmentController.cs`, `SkyLook.cs` | Time of day: the sun and moon, day and night brightness; the sky and the ambient light. |
| `plugin/src/Systems/VolumeLayer.cs` | Post-processing: colour grading, haze, lens effects, City night bloom, the photo filter. |
| `plugin/src/Systems/PhotoFilter.cs` | The photo filter and its colour table. |
| `plugin/src/Systems/HudScaler.cs` | The `[HUD]` sizes for the screen's HUD, applied just before it's drawn. |
| `plugin/src/Systems/HudPatches.cs` | The `[HUD]` sizes for the markers, name tags and pop-up words, which the game sizes every frame. |
| `plugin/src/Systems/MaterialTuner.cs` | Shadow tint, water and ice. |
| `plugin/src/Systems/SunShadows.cs` | Sharper, longer sun shadows, never below the game's own. |
| `plugin/src/Systems/AmbientOcclusion.cs`, `GrassFreeDepth.cs` | Ambient occlusion, worked out without the grass blades. |
| `plugin/src/Systems/GrassToggle.cs` | Hides the grass blades. |
| `plugin/src/Systems/EffectLights.cs` | Light from explosions, muzzle flashes and trails. |
| `plugin/src/Systems/LightningLights.cs` | Light from lightning strikes. |
| `plugin/src/Systems/CityNight.cs` | City windows, vehicle lights and lampposts at night. |
| `plugin/src/Systems/BallGlow.cs` | Glowing balls at night. |
| `plugin/src/Systems/LightPool.cs` | Hands the lights out each frame. |
| `plugin/src/Systems/Weather.cs`, `WeatherSky.cs` | Which weather is on: one at a time, and F10's order; the sky under it, for all three. |
| `plugin/src/Systems/FallingWeather.cs`, `Rain.cs`, `CartoonRain.cs`, `Snow.cs`, `ParticleLayer.cs` | What the falling weathers share; rain, cartoon rain, snow; and their particles. |
| `plugin/src/Systems/Footprints.cs` | Longer-lasting footprints in snow. |

## Maintainers

lmkHenlo, <ps.meiisbei@gmail.com>

## Contributing

Questions, bug reports and contributions are welcome by email to <ps.meiisbei@gmail.com>. Please attach `BepInEx/LogOutput.log` when reporting a problem, and a screenshot if something looks wrong.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `feat:`, `fix:`, `perf:`, `refactor:`, `style:`, `docs:` and `chore:`, with `BREAKING CHANGE:` for config settings that are removed or renamed. [CHANGELOG.md](CHANGELOG.md) follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

If you edit this README, please follow the [standard-readme](https://github.com/RichardLitt/standard-readme) specification.

## License

[MIT](LICENSE) © 2026 lmkHenlo
