# LightGreen testing

Each step lists what changed, how to build it, and the checks. Results are added under each step.

## Step 1: the base

LightGreen loads with no visual effects: settings, the ToggleKey comparison, and the feature host. The earlier
LightGreen has to be replaced, not kept beside it (same id).

**Build:** plugin only.

```
pwsh ./build.ps1 -Project plugin -BepInExCore "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\core" -InstallTo "$env:APPDATA\r2modmanPlus-local\SuperBattleGolf\profiles\Default\BepInEx\plugins"
```

The install folder is the same `BepInEx/plugins/LightGreen` as before, so the new `LightGreen.dll` replaces the old one.
The old `lightgreen.bundle` left in that folder is unused and can be deleted.

| # | Check | How | Pass if |
|---|---|---|---|
| S1 | Loads | Start the game, open `BepInEx/LogOutput.log` | `LightGreen 0.1.0 loaded: Super Battle Golf …, Unity 6000.3.27f2. 0 features. F8 compares with the game's own look.`; no warning about the Unity version; no errors |
| S2 | Looks like the game | Play a hole | Exactly the game's own look (no effects yet) |
| S3 | ToggleKey | Press F8 twice on a hole | `LightGreen: off (the game's own look)`, then `LightGreen: on`, shown top-left and in the log |
| S4 | Settings reload | Change `ToggleKey` to `F6` in the config and save, while playing | `Config reloaded.`; F6 now toggles, F8 doesn't |
| S5 | Disabled | `Enabled = false`, save | `Disabled (General.Enabled = false): the game's own look.`; the key does nothing; `true` again logs `Enabled.` |
| S6 | Quit | Quit the game | No errors at shutdown |

Put `ToggleKey` back to `F8` after S4.

**Results (2026-11-18):** S1 passes: `LightGreen 0.1.0 loaded: Super Battle Golf 1.4.0, Unity 6000.3.27f2. 0 features. F8 compares with the game's own look.`, no warnings or errors (the game reports its version as 1.4.0). S2–S6 then pass: F8 toggles both ways (and F6 once set, S4), `Disabled …` and `Enabled.` follow the setting, no errors through to quitting. Each config save logs `Config reloaded.` twice.

## Phase 1: world look

Ported from the earlier LightGreen, with the parts that needed its own shaders left out: time of day (F9), colour
grading, distance haze, lens flare, tilt-shift (hole overview), photo-mode depth of field, vignette, chromatic
aberration, shadow tint, water tuning, ice glints, and sharper, longer sun shadows. All run in the feature
host: each is on only on a hole, off with F8 or `Enabled = false`, and puts everything back as the game had it.
Also: no shader bundle (the build script refuses `-Project bundles`), and a config save now logs
`Config reloaded.` once instead of twice.

Not carried over (they needed LightGreen's shaders): rim light, contact-hardening shadows, and the `_LG_` values
the rebuilt shaders read. Not carried over for now: the `General.Look` presets (Enhanced/Full/VrComfort).

**Build:** plugin only (same command as step 1). The presets file from the earlier version (`LightGreen.presets.json`)
is reused as it is.

| # | Check | How | Pass if |
|---|---|---|---|
| P1 | Log | Start the game, play a hole | `[ok] Hook: …` lines for scenes, hole, graphics settings and camera mode; `LightGreen 0.1.0 loaded: … 4 features. F8 …, F9 …` (11 from Phase 2); `Biome: …`; `Scene lighting captured: …`; `Materials tuned: N toon, N water, N terrain.`; `Sun shadows on …: 4096 px, 200 m (game: …)`; no errors |
| P2 | Time of day | F9 through every time on a Forest hole | Sun moves and recolours; sky, light and haze follow, blending over 3 s; `Time of day: …` top-left and in the log; Night is dark but readable |
| P3 | Grading and haze | Compare with F8 on Forest, Desert, Snow, City | Each biome's own slight colour; distant terrain fades toward the horizon colour; no hard fog line |
| P4 | Shadow tint | Shade on buildings and props, F8 both ways | Shadows lean cool in Snow, warm in Desert; players and their cosmetics keep their colours |
| P5 | Water and ice | A Coast hole (water), a Snow hole (snow terrain) | Sun highlights on water that follow the time of day; snow sparkles more |
| P6 | Sun shadows | Look at shadows near and far, F8 both ways | Sharper near shadows, shadows reaching further; F8 off: the game's own |
| P7 | Lens flare | Look toward a low sun (Sunset) | Streaks and ghosts; none at Night or with the sun behind |
| P8 | Overview and photo | Hole overview flyover; photo mode | Overview looks like a miniature (tilt-shift); photo mode softens away from the centre; both fade in and out |
| P9 | Lens extras | `Vignette = 1`, `ChromaticAberration = 1`, then back to 0 | Each shows, then goes |
| P10 | Off and back | F8; `Enabled = false`; leave to the menu; next hole | Each time the game's exact look, then LightGreen's again; no errors |
| P11 | Settings reload | Save the config once | `Config reloaded.` once |
| P12 | Graphics settings | Change the game's shadow quality in its menu, on a hole | Log shows `Sun shadows on …` again with the new game value; shadows stay sharp |
| P13 | Frame rate | Busy hole, F8 both ways | Close to the game's own |

**Results (2026-11-18):** P1–P8 and P10–P13 pass. P9: vignette and chromatic aberration show and go;
film grain at 1 showed nothing visible, and has since been removed (setting and effect). Its leftover `FilmGrain`
line is dropped from existing config files when the game starts.

The log check found:
- F9 sometimes followed by `Config reloaded.` (F9 saves the config file and the reload check took that as an edit).
  Fixed: F9's own save is no longer reported.
- Each F8 "on" repeated `Scene lighting captured`, `Materials` and `Sun shadows`. Fixed: lighting is logged once
  per biome (again only if it differs), sun shadows only when the values change, materials once per biome.
- City and Coast logged `Materials:` twice at the start of a hole (the hole-change rescan after the biome one).
  Fixed with the above. The line is now `Materials tuned: …`; the counts grow over a session because the game
  keeps earlier holes' material copies loaded, which is expected.
- After changing the game's shadow quality, `Sun shadows on Default_PipelineAsset … (game: 4096 px …)` is the
  game's new value, as expected (P12).

**Retest after these fixes:**

| # | Check | How | Pass if |
|---|---|---|---|
| P9b | Film grain gone | Start the game once, then open `com.lightgreen.cfg` | No `FilmGrain` line under `[Effects]`; no warning about tidying settings in the log |
| P14 | Quieter log | Play two holes; F9 a few times; F8 off and on a few times | No `Config reloaded.` after F9; one `Scene lighting captured` and one `Materials tuned` per hole start; F8 adds only `off …` / `on` |

**Results (2026-11-18):** P9b passes: no `FilmGrain` line left in the config. P14 passes: F9 through the whole
day twice with no `Config reloaded.`; one `Scene lighting captured` and one `Materials tuned` line per
hole start, `Sun shadows` once; F8 adds only `off …` / `on`; no warnings or errors. The log showed `captured:sun` and
`Default_PipelineAsset:4096` with the space after the colon missing (lost in the log change); restored.

## Phase 2: light

Ported from the earlier LightGreen without anything of its own shaders: ambient occlusion (the game's HTrace AO,
turned on for every course as GTAO after opaque objects, with HTrace's own composition), worked out from a depth
drawn without the grass blades (the game's own depth-only passes); the grass toggle; effect lights; ball glow;
lightning; City night (lit windows and vehicles, lampposts, bloom). The lights share a pool of 24 URP lights, at most
two with shadows, handed out after every feature has asked; URP draws them only with the game's Dynamic Lighting
setting on. Each is a feature in the host: on only on a hole, off with F8 or `Enabled = false`, and everything put
back as the game had it.

Not carried over: the AO composite and grass-mask debug view, which were LightGreen shaders; lamp shadows (the
game's toon shader doesn't draw extra-light shadows); cloud shadows. City night's glowing windows only use the
keyword combinations the game's toon shader has a glowing variant of (checked against 1.4.0-798); others stay dark
and the log counts them.

**Build:** as before. New settings appear in `com.lightgreen.cfg` on first start: `Effects.AmbientOcclusion`,
`EffectLights`, `BallGlow`, `BallGlowOwnOnly`, `Lightning`, `CityNight`, `Shading.Grass`,
`Quality.AmbientOcclusionQuality`. For the lights, turn on the game's
Dynamic Lighting setting.

| # | Check | How | Pass if |
|---|---|---|---|
| L1 | Log | Start the game, play a hole | `[ok] Hook: Effects played`, `Lightning strikes`, `Grass drawing`, `Grass material`, `Ambient occlusion depth`; `… 11 features …`; on the first hole `Ambient occlusion: on, Medium quality.` and `Ambient occlusion: worked out without the grass blades.`; no errors |
| L2 | Ambient occlusion | Forest, Desert, City and Coast holes, F8 both ways | Soft darkening under carts and benches, at the foot of posts, trees and walls, in building corners; open grass even, no dark specks; F8 off: the game's own look |
| L3 | AO setting gone | Open `com.lightgreen.cfg` after starting the game | No `AmbientOcclusionIgnoresGrass` line under `[Quality]` (grass is always left out of the occlusion) |
| L4 | AO quality | `Quality.AmbientOcclusionQuality` Low, Medium, High while playing | Each applies at once (log line per change); Low a little softer and cheapest, High sharpest; no flicker or trailing smears when the camera moves |
| L5 | Snow | A Snow hole, F8 both ways | LightGreen's AO in place of the course's faint one; nothing darkened twice (no dark halo that lags behind moving objects) |
| L6 | Grass toggle | `Shading.Grass = false`, F8 both ways, next hole, then `true` | Blades gone at once (terrain colour shows), `Grass: hidden …` once; back with F8 off and outside a hole; `true`: back, `Grass: drawn again …` once |
| L7 | Effect lights | Dynamic Lighting on; mines, rockets, orbital laser, shotgun, pistol, freeze bomb, rocket driver | Explosions light the ground and players for under a second, the big ones with shadows; muzzle flashes pop; rocket and freeze-bomb trails carry a moving light; the game's Explosion flashes off: slower, half as bright |
| L8 | Ball glow | Night (F9), own ball and others'; `BallGlowOwnOnly = true`; then Noon | Each ball glows evenly all over in its own colours (no lit top half), no light pool on the ground; own brightest; own only with the setting; none by day, balls exactly as the game draws them |
| L9 | Lightning | Thunderstorm item, by day and at Night; Explosion flashes off | Each strike lights where it lands (with a shadow) and the ground round it, flickering two or three times, far stronger at night; flashes off: one gentle pulse |
| L10 | City night | A day City hole (Crosswalk, Uptown, Central Park…), F9 to Night, then back to Noon | `City night: N materials glow (…); M lampposts lit…` with N and M above 0; windows and vehicle lights glow, lampposts light the road below, bright lights bloom; bus stops and traffic lights unlit; by day everything as the game has it |
| L11 | Dynamic Lighting off | Turn the game's Dynamic Lighting off, repeat L7 and L10 | No lights (expected), windows still glow, no errors |
| L12 | Off and back | F8; `Enabled = false`; leave to the menu; next hole | Each time the game's exact look (AO, grass, windows, lights all back); no errors |
| L13 | Frame rate | Busy hole, AO Medium, F8 both ways; then AO Low | Close to the game's own; Low cheaper |

**Found while testing (2026-11-18):** soft dark patches on City roads, as if objects' shadows showed through the
ground (screenshot of a crosswalk). Cause: the depth without grass drew only each shader's DepthOnly pass, and the
game's toon shader (roads, buildings, most props) has none (only ForwardLit, ShadowCaster, DepthNormals), so the
road was missing from it and the occlusion saw what lies under the road. Fixed: the depth is drawn with the
DepthNormals, DepthNormalsOnly and plain unlit passes, which every opaque world shader in the game has one of.
Retest L2 on a City hole (roads, crosswalks, pavements) and on grass.

**Changed (2026-11-18):** ball glow lit only the top half of the ball (screenshot: a light 0.35 m above a toon ball
gives a hard lit cap). Now the ball glows evenly with the game's own toon emission (keyword on the ball material when
the build has that variant and its emission colour is black; colour and texture per ball through a property block),
and the light sits at the ball's centre so it only lights the ground. Balls whose material can't glow keep the light
and are named once in the log (`Ball glow: … doesn't glow …`). Retest L8.
Then the light on the ground under the ball was too bright; of six strengths offered, the choice was none (option F),
so ball glow no longer uses a light at all: only the even glow. It now works with Dynamic Lighting off too.

**Results (2026-11-18):** L1–L13 pass, after the fixes above (the depth without grass drawn with the passes the toon
shader has; `Quality.AmbientOcclusionIgnoresGrass` removed, grass always left out; ball glow even and without a ground
light). Phase 2 is complete.

**Known limitation (2026-11-18):** with ambient occlusion on, slightly jagged dark lines show where surfaces meet or
overlap (beam joints, between leaf clumps). The occlusion is worked out per pixel and laid over the anti-aliased
image, because the game's toon shader has no variant that reads it while drawing. Accepted as is.

## Phase 3: weather

Ported from the earlier LightGreen: longer-lasting footprints in snow (blue-grey, fading at the end, capped, cleared on
a new hole, short for a player a smoke bomb hides from you), and rain without the wet look (streaks, splashes, a grey
overcast sky and closer fog; off by default). The rain is drawn with a copy of the game's own particle material
(or URP's particle shader if that isn't loaded). Not carried over: wet surfaces, puddles, puddle reflections and the
wet-ground decal, which were LightGreen shaders.

**Build:** as before. New settings: `Effects.Rain` (0), `Effects.Footprints`,
`Effects.FootprintSeconds`, `Effects.FootprintMax`.

| # | Check | How | Pass if |
|---|---|---|---|
| W1 | Log | Start the game, play a Snow hole | `[ok] Hook: Footsteps` and `Footprint effects`; `… 13 features …`; after walking on snow `Footprints: snow prints on this hole last 45 s (up to 300).`; no errors |
| W7 | Footprints | Walk and run on snow, then on sand (Desert) | Snow: blue-grey prints that last about 45 s and fade at the end; sand: the game's own short prints |
| W8 | Footprint settings | `FootprintSeconds = 10`; `FootprintMax = 20` and walk a long way; `Footprints = 0` | Prints last 10 s; past 20 the oldest fade over a second (log line once); 0: the game's prints, existing ones fade |
| W9 | Footprints and holes | Leave prints, go to the next hole; F8 | New hole: none left from the last; F8 off: prints fade and new ones are the game's |
| W10 | Rain | `Effects.Rain = 1` on Forest, City, Snow, driving range | `Weather particles: drawn with the game's own particle material` once; streaks slanted with the wind, splashes round you on the ground, the sky and sun turn grey and the fog closes in over about 10 s; `Rain: on, strength 1.`; surfaces stay as the game draws them |
| W11 | Rain off and holes | `Rain = 0` mid-hole; then `1` and a new hole; F8 | 0: streaks stop, the sky clears over about 10 s; a new hole in the rain starts grey at once; F8 off: the game's own sky |
| W12 | Rain at night | Rain at Night | Streaks dimmer, no bright white lines |
| W13 | Off and back | `Enabled = false`; leave to the menu; next hole | No rain or long prints in the menus; everything back on the next hole; no errors |
| W14 | Frame rate | Rain at 2, F8 both ways | Close to the game's own |

**Changed (2026-11-18):** snowfall removed before testing: not worth its cost in frame rate. `Effects.Snowfall` is
dropped from existing config files on start. Tests W2–W6 (snowfall) removed.

**Results (2026-11-18):** W1 and W7–W14 pass (W2–W6 went with snowfall). Phase 3 is complete.

## 0.1.1: rain toggle

**F10** (`Keys.RainKey`) switches the rain off and on. It sets `Effects.Rain` to 0, or back to the last strength
above 0 (1 if there was none since the game started), and saves the config file, as F9 does for the time of day.
Nothing happens while F8 has LightGreen switched off.

**Build:** as before. New setting: `Keys.RainKey` (`F10`).

| # | Check | How | Pass if |
|---|---|---|---|
| R1 | Log and toggle | Start the game (`Rain = 0`), play a hole, F10, then F10 again | Load line ends `F10 switches the rain.`; first F10: `Rain: on (1)` top-left, `Rain: on, strength 1 (RainKey).`, rain starts and the sky greys over about 10 s, the config file shows `Rain = 1`; second F10: `Rain: off`, rain stops and the sky clears, `Rain = 0` |
| R2 | Strength kept | Set `Rain = 1.5` in the config, then F10 twice | Off, then back on at 1.5 (`Rain: on (1.5)`) |
| R3 | With F8 | F8 (LightGreen off), F10, F8 | F10 does nothing while off; the rain is as it was before F8 |
| R4 | Rebind | `RainKey = F7`, then `None` | F7 switches the rain and F10 doesn't; `None`: no key switches it |
| R5 | Config still reloads | After R1–R4, change another setting (e.g. `Time`) in the config while playing | `Config reloaded.` once; no `Config reloaded.` after an F10 press |
| R6 | No hitch | F10 a few times in windowed, borderless and fullscreen | No pause, freeze or lost input after F10 (Windows treats F10 as a menu key) |

Put `RainKey` back to `F10` after R4.

**Results (2026-11-18):** R1–R6 pass. The log shows `… F10 switches the rain.` on load; F10 on (`Rain: on, strength 1
(RainKey).`, then the rain's own `Rain: on, strength 1.`) and off on Forest and Snow holes; after `Rain = 1.5` was
saved (`Config reloaded.` once), F10 off and back on at 1.5; F8 off and on leaves the rain as it was; no
`Config reloaded.` after any F10 press; no warnings or errors.

## 0.1.1: stronger effects

Effect strengths go past 2. Each has its own most, set from where its code stops making a difference (its own
limit) or where it starts to look broken, flash too hard or cost too much:

| Up to | Effects | Why there |
|---|---|---|
| 4 | `ShadowTint`, `DistanceHaze`, `LensFlare`, `PhotoDepthOfField`, `Vignette`, `ChromaticAberration`, `AmbientOcclusion`, `EffectLights`, `CityNight` | Shade is fully the biome's hue at 2.5–4; haze colour reaches the horizon's by about 4 (fog distance stays as at 1); the lens is fully open at 4; vignette and fringes are full at 4; AO intensity 2 |
| 3 | `ColorGrading`, `WaterTuning`, `IceGlints`, `BallGlow`, `Lightning`, `Rain` | Night exposure −0.75 stop at 3; water glare; snow shimmer; ball colours wash toward white; lightning flashes; rain costs 1.5× the drops of 2 |
| 2 | `TiltShift`, `Footprints` | Already at their own limit at 2 (widest lens; darkest prints) |

**Build:** as before. Each `Effects.*` entry now shows its own range (`# Acceptable value range: From 0 to N`).

| # | Check | How | Pass if |
|---|---|---|---|
| X1 | Config | Start the game, open the config | Each effect shows the range above; your values are kept; no warnings or errors |
| X2 | Too high | Set `Lightning = 5` and save | Treated as 3 (the file may show 3 after the reload); no errors |
| X3 | Shade and colour | `ShadowTint = 4`, `ColorGrading = 3`, `DistanceHaze = 4` on Snow and Desert; F9 to Sunset and Night | Strong but readable: shade a clear biome hue, no broken textures; Sunset warm, Night dark but playable; distant terrain takes the horizon's colour |
| X4 | Light | `EffectLights = 4`, `Lightning = 3` (Explosion flashes on), `CityNight = 4` on a City hole at Night | Bright pops and strikes that aren't uncomfortable to look at; windows and lamps brighter; no errors |
| X5 | Glow and glints | `BallGlow = 3` at Night with other players; `IceGlints = 3` and `WaterTuning = 3` on Snow and Coast | Balls bright and still told apart by colour; sparkle and water highlights strong without heavy shimmer or glare |
| X6 | Occlusion and lens | `AmbientOcclusion = 4`; `LensFlare = 4` toward a low sun; `Vignette = 4`, `ChromaticAberration = 4`; `PhotoDepthOfField = 4` in photo mode | Darker contact shadows, halos and noise acceptable; strong flare, full vignette and fringes, strongest photo blur |
| X7 | Rain cost | `Rain = 3`, F8 both ways, then `Rain = 2` | Heavier rain; frame rate acceptable next to 2 |

If an effect looks wrong at its most, note the highest value that looks right: its most can come down to that.

**Results (2026-11-18):** X1–X7 pass. Each effect's most stays as set.

## 0.1.1: no yellow snow

**Found (2026-11-18):** on a Snow hole at Dawn, Morning, Golden hour and Sunset, snow and ice turned yellow-green
where they faced sideways or down (undersides of mounds, the arch, the lower back wall); BlueHour gave the tops a
deep blue band instead. Sampled at the bottom of one mound: the game's own (107, 143, 165), with LightGreen
(158–176, 176–179, 120–122): red and green up, blue down a quarter.

**Cause:** the time of day shifted the hole's ambient (fill) light by the change in sky colour: the horizon's
change from every direction, the sky's from above. The warm presets' horizons (orange, peach, pink) minus the
Snow holes' cold blue one is "more red and green, less blue", and that was added to every face; only the tops
got the blue sky change on top of it. Stronger the more of the preset's sky is blended in (Sunset, Dawn, Golden
hour), nearly none at Noon (light-blue horizon, little blend), none at the hole's own time.

**Fix:** the shift is brightness only: how much brighter or darker the preset's sky and horizon are than the
hole's, added as grey, in the same shape as before. The fill light keeps the hole's own hue, and each time's
`ambientTint` still colours it (blue at BlueHour and Night). Sky, sun, moon and fog are unchanged.

| # | Check | How | Pass if |
|---|---|---|---|
| A1 | Snow | The Snow hole from the screenshots, F9 through every time | No yellow or green on undersides, the arch or the back wall at any time; snow and ice stay blue-white |
| A2 | Hole's own time | The same hole at `HoleDefault`, F8 both ways | The lighting is identical |
| A3 | Dark times | BlueHour and Night on Snow and Forest | Still dark and blue-tinted; not grey or washed out |
| A4 | Warm shade | Forest and Desert at Golden hour and Sunset | Lit sides warm; note whether the shade looks right or too cold |
| A5 | Presets | Edit `LightGreen.presets.json` while playing | `Presets reloaded.` and the change shows; no errors |

If A4's shade looks too cold: give Dawn, Morning, Golden hour and Sunset a gentle warm `ambientTint` (about
1, 0.94, 0.86 at Golden hour) and raise the presets version from 6 to 7, so existing files are backed up and
replaced with the new defaults.

**Results (2026-11-18):** A1–A5 pass. A4: the shade at Golden hour and Sunset looks right, so no warm `ambientTint`
was added and the presets stay at version 6.

## 0.1.1: day and night brightness

`Brightness.Day` (0.5–1.5) and `Brightness.Night` (0.25–1.5), both 1 by default, blended by how dark the scene is
(the darkness ball glow and City night use, worked out from the sun before this scaling, so a darker day never
switches night things on). The result multiplies the sun or moon, the fill light, the sky's colours and the fog
colour together, at every time of day including the hole's own. Lights and glows aren't scaled. Brightening past
`MaxLightBrightening` (1.5 for now, so never) is done with exposure instead.

**Build:** as before. New settings: `Brightness.Day`, `Brightness.Night`.

| # | Check | How | Pass if |
|---|---|---|---|
| B1 | Brighter day | `Day = 1.5` at Noon on Forest | Lit sides clearly brighter; note whether they stop brightening before 1.5 (that sets `MaxLightBrightening`) |
| B2 | Darker day | `Day = 0.5` at Noon | A dimmer day; ball glow and City night stay off |
| B3 | Darker night | `Night = 0.25` at Night on a City hole | Dark surfaces; windows, lampposts and ball glow still bright |
| B4 | Hole's own night | `Night = 0.5` at `HoleDefault` on a night hole (Snow 13, Causeway) | Darker than the game's own |
| B5 | No change at 1 | Both 1, F9 through every time, F8 both ways | As before |
| B6 | Dusk | `Day = 1.5`, `Night = 0.25`, F9 through every time, then `ProgressionMinutes = 2` | Smooth from day to night; no jumps |
| B7 | Sky and fog | Any of the above, looking at the sky and far terrain | Sky and fog match the ground; nothing glows in the distance |
| B8 | Clipping | `Day = 1.5` on Snow and Desert | Brighter; snow not blown out to plain white |
| B9 | Reload and F8 | Change both while playing; F8 | Applies within a second; F8 shows the game's own look |

Put both back to 1 and `ProgressionMinutes` to 0 afterwards.

**Results (2026-11-18):** B1–B9 pass. B1: lit sides keep brightening up to 1.5, so `MaxLightBrightening` stays 1.5
and brightening is all done with light (exposure is never used at these ranges).

## Photo filters

A filter's colours are built into a lookup table (the pipeline's grading size, 32³ by default) and handed to URP's
Color Lookup, which applies it last: after LightGreen's grading and tonemapping, on display-encoded colours. So a
filter sits on top of the time of day and biome look instead of replacing it, and the HUD (drawn after
post-processing) isn't touched. Lens extras (vignette, distortion, fringes, bloom) are mixed with LightGreen's own:
the stronger vignette and fringes win, bloom adds. No film grain. `Filters.Filter`, `Filters.Strength`,
`Keys.FilterKey` (F11; Shift+F11 back). Files: `LightGreen.filters.json` and the `LightGreen.filters` folder for `.cube` files.

**Checked before the build (outside the game):** an empty filter gives the identity table (largest error 6×10⁻⁸);
the table read back the way URP samples its strip changes nothing (0 of 255); tone curves pass through their points
without overshoot; an inverting 17³ `.cube` and a 1D `.cube` read back exactly, also through the strip; five broken
files are each rejected with a message. Every built-in filter was applied to a game screenshot and a colour chart:
each looks as named (Teal & Orange was strengthened after this check).

**Build:** as before. New: `Filters.Filter`, `Filters.Strength`, `Keys.FilterKey`; `BepInEx/config/LightGreen.filters.json`
and the `BepInEx/config/LightGreen.filters` folder are made on first start.

| # | Check | How | Pass if |
|---|---|---|---|
| F1 | Load and cycle | Start; F11 a few times; Shift+F11 | Log: `Photo filters: 13 in LightGreen.filters.json, 0 from .cube files in LightGreen.filters.`, and the load line ends `F11 changes the photo filter.`; each press shows `Filter: <name>` top-left, logs `Photo filter: <name> (FilterKey).` and `Photo filter: <name> (32³ colour table).`, and `Filters.Filter` changes in the config; Shift goes back; after Dreamy comes None |
| F2 | Each filter | Every built-in on Forest at Noon, Snow, and a City hole at Night | Each looks as in the README table; the HUD is unchanged |
| F3 | Strength | `Filters.Strength` 0, 0.5, 1 with Sepia | None, half, all of it; no jumps |
| F4 | On top of the look | A filter on; F9 through every time; another biome | The filter stays; the time of day still shows through it |
| F5 | Your own LUT | Put a `.cube` file in `LightGreen.filters`; then a broken one (e.g. delete half its lines) | `Photo filters reloaded: 13 in …, 1 from .cube files …`; it joins the F11 cycle under its file name and looks as it does in a photo editor; the broken one: `Skipped the photo filter <file>.cube: <reason>.` |
| F6 | Editing filters | Change a filter's numbers in the JSON while it's on; then break the JSON (delete a brace) | The change shows within a second; broken: `Couldn't reload the photo filters (…); keeping the previous ones.` |
| F7 | Lens extras | Toy Camera; Dreamy; then None with `Effects.Vignette = 2` | Toy Camera: dark corners, a bulge with no smeared or black edges, slight fringes; Dreamy: glow around bright parts; None: LightGreen's own vignette still works |
| F8 | Compare | F8 with a filter on, then F8 again | The game's own look, then the filter back |
| F9 | Photo mode | Take a photo in the game's photo mode with a filter on | The saved photo has the filter (if not, note it) |
| F10 | Graphics quality | Change the game's graphics quality with a filter on | The filter stays and looks the same |

Put `Filters.Filter` back to `None` (or your favourite) afterwards.

**F1, first run (2026-11-19): failed.** `Photo filters: 0 in LightGreen.filters.json`: every F11 chose None. Both
`LightGreen.filters.json` and `LightGreen.presets.json` were 20 bytes, `{"version": N}` only. Unity's JsonUtility only
serializes types from the game's own scripts, so with LightGreen's classes it silently wrote (and read) nothing but
the top-level number. The presets file has been like this since 0.1.0: the built-in presets were always used, and
editing the file did nothing.

**Fix:** LightGreen's own JSON (`Core/Json.cs`) for both files: public fields by reflection, colours as `[r, g, b]`,
missing fields keep their defaults, `//` comments and trailing commas allowed, errors give the line and column (or the
field, e.g. `filters[0].saturation`). A file holding only the version number is replaced with the full one on the next
start (the presets one kept as `.v6.bak`). Checked outside the game: presets and filters round-trip field for field and
write identically twice; the 20-byte file is recognised; partial files keep defaults; six kinds of broken file are
rejected with where and why.

| # | Check | How | Pass if |
|---|---|---|---|
| J1 | Files rewritten | Start the game once | Log: `Wrote the full presets to … The old file is at …v6.bak.` and `Wrote the built-in photo filters to … (the file had only a version number).`, then `Photo filters: 13 in LightGreen.filters.json, …`; both files now list everything |
| J2 | Presets edit | At Noon, set `Noon`'s `sunIntensity` to 2 in `LightGreen.presets.json` and save; then back to 1.05 | `Presets reloaded.` and the sunlit sides brighten, then return |
| J3 | Broken presets | Delete a comma in `LightGreen.presets.json` and save; then put it back | `Couldn't reload the presets (line …, column …: …); keeping the previous ones.`; the look doesn't change; fixed, it reloads |

**Results (2026-11-19):** J1–J3 pass. J3 also holds with the file renamed.

**Results (2026-11-19), after the fix:** F1–F10 pass. Photo filters are complete.

## More photo filters

Fourteen more built-in filters (sixteen at first; Infrared and Fisheye were dropped after testing), placed by family, so F11 goes: Mono, Mono Red, Noir, Sepia, Cyanotype; Faded Film,
Instant Film, Bleach Bypass, Two-Strip; Vivid, Warm, Cool, Teal & Orange, Cross Process, Day for Night;
Duotone, Poster, GameBoy, Solarize, Negative, Thermal, Night Vision; Toy Camera, Dreamy; Protanopia,
Deuteranopia, Tritanopia. All use what filters could already do: no new settings or code. The built-in filters'
version goes from 1 to 4, so an existing `LightGreen.filters.json` is backed up and replaced on the next start.

- **GameBoy** (first called Handheld Console): black and white, a tone curve with four flat steps, then a four-colour gradient map.
- **Solarize** and **Negative:** tone curves that fall (the curves already allowed that).
- **Colour blindness:** channel mixer matrices from Machado, Oliveira and Fernandes (2009) at full strength. They're
  meant for linear colour and the filter works on display colours, so they're close, not exact. The HUD isn't filtered.

**Checked before the build (outside the game):** the offline table checks pass as before; all 27 filters save and load
back field for field. Every new filter was applied to a Forest-by-day and a City-at-night screenshot and looks as
named (Cyanotype's middle blue was lightened, GameBoy's steps moved down to
suit the game's brightness, and Night Vision's brightness lowered, after this check). The lens extras can't be
previewed this way.

**Build:** as before. Nothing new in the config.

| # | Check | How | Pass if |
|---|---|---|---|
| G1 | Filters file updated | Start the game once | Log: `Updated the photo filters to LightGreen's new built-in ones (version 4). Your previous file is at …v3.bak.` (or …v1.bak, …v2.bak from an older one), then `Photo filters: 27 in LightGreen.filters.json, …`; F11 goes in the order above, then your `.cube` filters, then None |
| G2 | Each new filter | Each new one on Forest at Noon, Snow, and a City hole at Night | Each looks as in the README table; the HUD is unchanged |
| G4 | Night Vision | Night Vision on a City hole at Night | Green, lit windows and lamps glow, dark corners |
| G5 | Strength | `Filters.Strength` 0.5 with Negative, then Solarize; then back to 1 | Half way, with no banding or jumps (Negative at 0.5 is flat grey; that's expected) |

Run G5 last, and put `Filters.Strength` back to 1 afterwards: every filter is weaker below 1, and the lens extras too.

**Results (2026-11-19), first run (16 new filters, version 2):** G1 passes (29 filters, the old file kept as
`.v1.bak`). `Filters.Strength` had been left at 0.5, so everything ran at half strength: all filters looked right
except Negative (flat grey, which is what half strength gives) and Fisheye (only a slight zoom: half the distortion,
with most of the zoom left). Infrared, seen at full strength, was harsh: the game's light cyan sky turned purple, dark
grass maroon, and reds bright green; the channel mixer can't keep the sky blue while turning grass red. **Infrared and
Fisheye were dropped** (version 3). Rerun G1, then G2 for Negative at `Filters.Strength` 1.

**Results (2026-11-19), second run (`Filters.Strength` 1):** every filter works as intended, Negative included. That test build turned out to be one step behind: it was compiled before its sources were updated, so it still had version 2 with Infrared and Fisheye, and the next one (meant to rename Handheld Console) had version 3 without the rename. Builds now compile straight from the repository. The removal and the rename (version 4) are checked with the 0.2.0 build: G1 shows `(version 4)` and `Photo filters: 27 in LightGreen.filters.json`, with GameBoy where Handheld Console was and no Infrared or Fisheye.

## Filter dropdown

r2modman's config editor shows a setting as a dropdown when its config file entry has a `# Acceptable values:` line
(it splits the line at commas and sorts it A–Z). `Filters.Filter` now has one: None and every filter LightGreen has
loaded. BepInEx's own list resets a value that isn't in it, which would undo a `.cube` added while playing, so
`FilterChoices` reads the names afresh each time and accepts any name. LightGreen saves the config file when the list
changes (at start, and when a filter is added, removed or renamed), first taking in any edit to the file so it isn't
written over, and takes that save as its own so it doesn't reload.

**Checked before the build (outside the game), with BepInEx 5.4.23.5's own `ConfigFile`:** the file lists None and the
27 built-in filters; a listed filter is kept; an unlisted name (a `.cube` added later) is kept, not reset; a blank
value becomes None; after a `.cube` appears, saving lists it, and so does the list in-game config editors read; a
missing name typed by hand survives a reload.

**Build:** as before, plus `Presets/FilterChoices.cs`.

| # | Check | How | Pass if |
|---|---|---|---|
| D1 | Dropdown | Start the game once, quit, open LightGreen in r2modman's config editor | `Filters.Filter` is a dropdown: None, the 27 built-in filters and your `.cube` filters, A–Z |
| D2 | Pick from it | Choose Noir in the dropdown and save, with the game running | Noir within a second (log: `Config reloaded.`); F11 goes on to Sepia |
| D3 | A `.cube` added while playing | Copy `Invert.cube` into `BepInEx/config/LightGreen.filters/` while playing | Log: `Photo filters: 27 in …, 1 from .cube files …`, and no `Config reloaded.` from LightGreen's own save; reopen the config editor: Invert is listed |
| D4 | A name that isn't there | F11 to Invert, quit, delete `Invert.cube`, start | `Filters.Filter` still says Invert (not reset); no filter shows; the log says Invert isn't a filter; the dropdown no longer lists it |
| D5 | An edit isn't lost | While playing, change `Effects.Rain` in the config file and save, then within a second copy a `.cube` into the folder | The rain change takes effect and stays in the file |

**Results (2026-11-21):** the user reports **D1–D5 pass**.

## Messages in the chat box

LightGreen's short messages (what F8–F11 or a config edit changed) went to the top-left corner, drawn with IMGUI. The
game has a text chat, and its `TextChatUi.ShowMessage` adds a line to this player's chat box only, with nothing sent
over the network: the game posts its own local lines that way (the chat-muted notice, the cheats warning in match
setup, vote-kick results). From the game's data, the chat box shows up to 7 new lines for 5 seconds each (then a
1.5-second fade) and keeps 100 lines of history; it is part of the game's managers, so it should exist for the whole
session, menus included.

- `General.Notices`: `Chat` (default), `Screen` (the top-left corner, as before) or `Off`.
- Chat lines are built the way the game builds a player's message (`TextChatUi`'s own code): the name through
  `GameManager.UiSettings.ApplyColorTag(name, TextHighlight.Regular)` (the game's highlight colour), `": "`, then the
  message through `GameManager.RichTextNoParse`, so it reads `LightGreen: Filter: Noir` like `lmkHenlo: asdf`, and a
  filter name with tags in it shows as typed. (First version: `LightGreen · <message>`, the name bold in LightGreen's
  own green; changed to match the game's lines.)
- Each kind of message (time, filter, rain, toggle) keeps only its latest text until no change has come for
  `General.NoticeDelay` seconds (0.5 by default, 0–2; 0 posts each at once). Screen mode stays instant.
- No chat box at that moment: the message goes to the top-left corner. If the call fails (a game update changed the
  chat), LightGreen logs one warning and uses the top-left corner for the rest of the session. The call sits in its
  own method so that failure is caught.
- LightGreen now references the game's `SharedAssembly` (where `TextChatUi.HasInstance` is defined), which every
  copy of the game has.

**Checked before the build (outside the game):** the line is built as above; a name with tags stays plain text; with
no UI settings yet, the name shows without colour; no chat box returns false with nothing posted; a passing error
(say, while a scene loads) returns false that time only, with no warning; a missing type or method (a game update)
returns false with one warning, then stays off for the session with no more warnings. The merging is checked in the game (N2, N3).

**Build:** as before, plus `Core/ChatNotices.cs`. No warnings.

| # | Check | How | Pass if |
|---|---|---|---|
| N1 | Chat line | On a hole, F9 | About half a second later, `LightGreen: Time of day: …` in the chat box, the name coloured like a player's; nothing top-left; it fades after about 5 seconds |
| N2 | Rapid presses | F11 ten times quickly | One line, with the last filter |
| N3 | Two kinds | F9 then F11 quickly | Two lines, the time and the filter |
| N4 | History | Open the chat | LightGreen's lines are in the history with players' messages |
| N5 | Driving range and single player | F9 there | A chat line |
| N6 | No chat box | F8 in the main menu or while loading | In the chat if the box exists there, otherwise top-left; no warning in the log |
| N7 | Screen and Off | `General.Notices = Screen`, then `Off`; F9 each time | Screen: the top-left text at once, as before. Off: nothing |
| N8 | Delay | `General.NoticeDelay = 0`, then F11 three times | Three lines, at once |
| N9 | Config edit | Pick a filter in r2modman's dropdown while playing | A chat line |
| N10 | Tags in a name | In `LightGreen.filters.json`, copy Mono's entry and name the copy `<b>Bold` (Windows doesn't allow `<` in a file name, so not a `.cube`); F11 to it | The line shows `<b>Bold` as typed |
| N11 | Others don't see it | In multiplayer, with a second player if you can | They see none of LightGreen's lines |
| N12 | VR | With SBGVR, F9 | Note whether the chat line shows in the headset |

**Results (2026-11-21):** the user reports **N1–N12 pass**.

**Results (2026-11-20), first look:** the lines appear in the chat box and stay in its history alongside players'
messages (`LightGreen · on`, `LightGreen · Rain: on (1)`, `LightGreen · Filter: Cyanotype`, then `lmkHenlo: asdf`).
At the user's request they now match the game's own lines (`LightGreen: …`, the name in the game's highlight colour);
LightGreen stays in the chat rather than the kill feed (`InfoFeed`, which would need a Harmony patch on private code).
Rerun N1–N12 with the new look.

## Snow

Cartoon snow, from the user's reference picture: soft, round, slightly lumpy white flakes of many sizes, a few big
blurred ones close to the camera, and a bright, pale blue sky. "Magical, not realistic." It cycles with the rain on
F10: clear, rain, snow, clear.

An earlier snowfall (Snow holes only) was dropped before testing in phase 3 for its cost in frame rate: about 8,200
see-through particles, many of them large and far away (much of the screen covered over and over). This one is built
to cost about what the rain does (about 3,800 particles, W14 passed), with few flakes big on screen:

| Layer | Box round the camera | Flakes at strength 1 | Size | On screen at most |
|---|---|---|---|---|
| Near | 14 × 8 × 14 m | about 40 | 6–11 cm, soft edge | 4% of the screen |
| Main | 50 × 24 × 50 m | about 3,600 | 4.5–8 cm, crisp edge | 1.2% |
| Far | 160 × 50 × 160 m | about 770 | 35–55 cm | 0.4% |

- **The flakes** are drawn in code (`Core/FlakeArt.cs`, Unity-free): two 2 × 2 atlases of 64-pixel flakes, crisp and
  soft, each flake round with three low lumps round its edge, flat white with a faint blue shade on its lower right in
  two soft steps. Each particle shows one of the four at random (texture sheet, fixed frame). They're drawn with a copy
  of the game's own particle material, as the rain is, its texture swapped for the flakes; nothing is shipped.
- **Motion:** falling at about 1 m/s, a slow wander (Unity's particle noise at its cheapest), turning slowly, drifting
  with the game's wind (slower than rain). A camera cut refills the view at once, as for the rain.
- **Magical:** the flakes are a little brighter than white (1.15), so the game's bloom gives them a soft glow; at
  night they dim to 0.35 and turn blue.
- **Sky:** the rain's overcast gets a snowy share (`EnvironmentController.Snowy`). Snow's sky goes toward a bright,
  pale blue (top, horizon and the glow round the sun), dimmed by how dark it is; the sun dims to 0.85 (rain: 0.45)
  and turns cold white; the fog closes in less than rain's (0.7 and 0.8 of its distances, rain 0.5 and 0.6) toward
  a bright blue-white. `Rain.cs` eases the sky for both, over 10 s (at once on a new hole), so rain and snow never
  pull it different ways; going from rain to snow, the grey turns pale blue over 10 s.
- **One weather at a time:** `Effects.Snow` above 0 keeps the rain off. Turning one on (F10 or a config edit) sets
  the other to 0; both on at start keeps the snow (logged). F10 steps clear → rain → snow → clear, each as heavy as it
  last was. The message: `Weather: rain (1)`, `Weather: snow (1)` or `Weather: clear`, one kind (`weather`) for the
  chat's merging.
- Nothing settles on the ground (that needed shaders), and the game's own footprints still show only on Snow holes'
  snow.

**Checked before the build (outside the game):** the flake atlases were drawn and looked at, crisp and soft (the
first lumps were too strong next to the reference and were halved). The particles, sky and fog are checked in the
game.

**Build:** as before, plus `Core/FlakeArt.cs` and `Systems/Snow.cs`; one more feature (Snow). No warnings.

| # | Check | How | Pass if |
|---|---|---|---|
| S1 | Log | Start the game | `… 15 features …` (one more than before); the load line says `F10 steps the weather (clear, rain, snow)`; no errors |
| S2 | The look | `Effects.Snow = 1` on a Forest hole by day | Round white flakes of many sizes, a faint blue shade on one side, swaying and turning as they fall, drifting with the wind arrow; a few big soft ones pass close by; the sky turns bright and pale blue over about 10 s, the fog blue-white, the grass a little less green and the scene a little colder; compare with the reference picture |
| S3 | F10 cycle | F10 four times from clear | Rain, then snow (the grey sky turns pale blue over about 10 s), then clear (it clears over about 10 s), then rain; chat: `Weather: rain (1)`, `Weather: snow (1)`, `Weather: clear` |
| S4 | One at a time | In the config file, set `Rain = 1` while it snows; then quit, set both to 1, start | The rain replaces the snow and `Snow` becomes 0; at start, snow shows, `Rain` becomes 0 and the log says why |
| S5 | Heaviness | `Snow = 3`, then `0.5` | Many more flakes, then few; F10 round the cycle brings snow back at the last value |
| S6 | Every course | Snow on Coast, Desert, City, Snow and the driving range | Flakes everywhere; on Snow holes the game's footprints still work |
| S7 | Night | Snow at Night, and on a City hole at night | Dim, blue flakes, no bright white; the sky dark, not pale |
| S8 | Camera | Switch to spectating or a flyover, and back | The view is full of snow at once after each cut, no empty moment |
| S9 | F8 and holes | F8 off and on; next hole in the snow | Off: no snow, the game's own sky; a new hole starts snowy at once |
| S10 | Frame rate | Snow at 1 and 3, then rain at the same values, F8 both ways each time | Snow at 1 costs about as much as rain at 1 (it has a little more particles, almost all small on screen); note the numbers |
| S11 | VR | Snow with SBGVR | Flakes show in the headset and none sit stuck to the view |

**Changed (2026-11-20):** the user asked for twice the flakes: every layer's density doubled (near about 40, main
about 3,600, far about 770; about 4,400 in all at strength 1, a little more than the rain's 3,800). The table above
gives the new counts. The near layer's 40 big flakes are the ones that cost the most to draw; if S10 shows a drop,
they're the first to thin.

**Changed (2026-11-20, after an in-game screenshot on a Forest hole):** the snow read as light, realistic flurries:
the main layer's flakes came out one or two pixels across, so their shape and blue shade never showed and they
vanished against white cloud; the sky looked close to clear, the far fog grey-green, and the saturated grass said
summer. So:

- the main layer's flakes are about 1.5× bigger (0.07–0.12 m, from 0.045–0.08) and may cover up to 2% of the
  screen (from 1.2%); the count is unchanged, so the cost barely moves;
- the sky moves further (0.9 of the way, from 0.75) toward a paler blue top (0.62, 0.76, 0.97);
- the fog moves further (0.9, from 0.75) toward a whiter blue-white (0.92, 0.95, 1);
- while it snows the grade is a little colder (white balance −12) and less saturated (−12), eased in and out with the
  sky and applied whatever `Effects.ColorGrading` is, like the weather's fog.

S2 gains the colder grade. Whether the sky in that screenshot had finished its 10-second change isn't known: if S2's
sky still looks clear after 15 seconds of snow, the sky tint isn't reaching that course's sky.

**Changed again (2026-11-20, second screenshot):** the sky came out measurably paler (its top from about
(0.35, 0.61, 0.85) on screen to (0.49, 0.69, 0.85)), the fog over the water pale blue-white and the grass a little
muted, so those work. Two things didn't:

- the flakes still read as dots: the main layer's are mostly 10–25 m away, so 1.5× bigger only took them from about
  4 px to 6 px. A fourth layer, "close", now fills a 20 × 20 × 10 m box round the camera with about 440 medium,
  crisp flakes (0.08–0.13 m, at most 2.5% of the screen), 15–35 px across at 5–10 m, where the lumpy outline and blue
  shade show. About 4,850 flakes in all at strength 1, roughly 10% more;
- the whiter fog turned the dark pines in the middle distance a milky grey-green, more a damp morning than bright
  snow. It now starts and ends further out (0.85 and 0.95 of the dry distances, from 0.7 and 0.8), keeping the
  whiter colour: the far distance stays white, the middle trees keep their contrast.

S10 should note the frame rate with the close layer.

## Cartoon rain

A third weather from the user's reference picture (a stylised forest in the rain): bold, long, thin white streaks all
slanting the same way, small two-ring ripples where drops land, and a soft blue mist with the colours still bright.
"More visible than rain, but still read as rain." F10 now steps clear → rain → cartoon rain → snow → clear (the
user's order); S3's cycle above gains the cartoon rain step.

- **Streaks:** a crisp white disc drawn in code (`Core/RainArt.cs`, Unity-free), stretched by the particle system
  along the fall into a tapered line, as in the reference. Next to the rain, about twice as wide and long on screen and
  nearly opaque (0.85 near, 0.7 mid, 0.35 far; rain 0.35, 0.25, 0.12), a little blue-white, dimming at night:

| Layer | Box round the camera | Streaks at strength 1 | Width | Length (fall × stretch) |
|---|---|---|---|---|
| Near | 30 × 16 × 30 m | about 1,150 | 2.5–3.5 cm | about 0.9 m |
| Mid | 90 × 40 × 90 m | about 970 | 6–8 cm | about 1.1 m |
| Far | 240 × 70 × 240 m | about 600 | 22–28 cm | about 1.4 m |

  About 2,700 in all, fewer than the rain's 3,800 (2,400 after the review below). They drift with the game's wind, and always lean at least 2.5 m/s
  (about 15°) so they slant even in a calm, as in the reference.
- **Ripples:** where the rain throws droplets, cartoon rain lays two white rings flat on the ground (the inner fainter),
  growing from a quarter of their size to 30–45 cm and fading over 0.5–0.7 s; about 400 a second within 15 m at
  strength 1, everywhere a drop lands (as the rain's splashes are), at most 30 ground checks a frame.
- **Mist:** a third share of the overcast (`EnvironmentController.Misty`, beside `Snowy`; the rest is rain's grey):
  the sky goes 60% of the way to misty blues, the fog closes in to 0.6 and 0.7 of its distances toward a soft blue, and
  the sun dims to 0.7 with its colour kept, so the colours stay bright. Eased with the others over 10 s; from one
  weather to another, the old kind's share hands over to the new one's.
- **One weather at a time:** `Systems/Weather.cs` says which is on (snow first, then cartoon rain, then rain, should more
  than one be set) and F10's order. Turning one on (F10 or a config edit) sets the others to 0; more than one on at start
  keeps that first one (logged). The message: `Weather: cartoon rain (1)`.

**Checked before the build (outside the game):** the streak disc and ripple were drawn and looked at (the disc
stretched as the particle system does, the ripple squashed as seen at an angle). `Weather` with BepInEx's own
`ConfigFile`: clear when all are 0; rain; cartoon rain over rain; snow over both; F10's order clear, rain, cartoon rain,
snow, clear; each kind's setting; below 0.01 counts as off. All pass.

**Build:** as before, plus `Core/RainArt.cs`, `Systems/Weather.cs` and `Systems/CartoonRain.cs`; one more feature
(Cartoon rain). No warnings.

| # | Check | How | Pass if |
|---|---|---|---|
| C1 | Log | Start the game | `… 16 features …`; the load line says `F10 steps the weather (clear, rain, cartoon rain, snow)`; no errors |
| C2 | The look | `Effects.CartoonRain = 1` on a Forest hole by day; compare with the reference and with `Rain = 1` | Long, thin, bright white streaks, clearly easier to see than the rain, all slanting one way; white two-ring ripples spreading where drops land; the air turns a soft blue over about 10 s with the colours still bright, not grey |
| C3 | F10 cycle | F10 five times from clear | Rain, cartoon rain (the grey turns misty blue), snow (pale blue), clear, rain; chat: `Weather: rain (1)`, `Weather: cartoon rain (1)`, `Weather: snow (1)`, `Weather: clear` |
| C4 | One at a time | In the config file set `CartoonRain = 1` while it snows; then quit, set `Rain`, `CartoonRain` and `Snow` all to 1, start | The cartoon rain replaces the snow, `Snow` becomes 0; at start snow shows, `Rain` and `CartoonRain` become 0, two log lines say so |
| C5 | Calm and wind | A calm hole, then a windy one | Streaks slant in a calm too; in wind they slant with the HUD's wind arrow |
| C6 | Ripples | Watch the ground, grass, sand, water, a slope | Rings lie flat and spread on each; none float in the air or sit under the ground |
| C7 | Night | Cartoon rain at Night, and on a City hole at night | Dimmer streaks and rings, no glaring white; the sky dark blue, not pale |
| C8 | Camera, F8, holes | A camera cut; F8 off and on; a new hole in cartoon rain | The view fills at once after a cut; F8 off: none of it and the game's own sky; a new hole starts misty at once |
| C9 | Frame rate | Cartoon rain at 1 and 3, then rain at the same values, F8 both ways | Cartoon rain at 1 costs about what rain at 1 does; note the numbers |
| C10 | VR | Cartoon rain with SBGVR | Streaks and rings show in the headset |

**Review (2026-11-20), first look in the game (screenshot, Forest "Timber" hole 2):** the mid and far streaks and the
blue mist read as in the reference, the colours stay bright. But streaks falling within a metre or two of the camera were
drawn as long white slashes across the edges of the view, like speed lines, and fanned out from below the centre
(perspective, stronger at a wide field of view), where the reference's lines run nearly parallel; and no ripples could
be seen on the tee in front of the player. Changed:

- **A clear zone:** the near layer fills a hollow cylinder (inner radius 3 m, outer 15 m) instead of a box, its axis
  tilted along the fall (wind over fall speed) and through the camera, so the wind doesn't carry streaks into it. The
  particle layer gained `Spec.Clearance` for this; the rain's and snow's layers keep their boxes. Near streaks: about
  869 (was about 1,150); about 2,445 in all.
- **Shorter near streaks:** two thirds of the length (stretch 0.06, was 0.09).
- **Ripples:** 50–75 cm (was 30–45), fully opaque at first, lasting 0.7–0.9 s, and drawn from both sides (`_Cull` 0):
  a flat quad facing down was likely culled, which would explain seeing none. The log says `Cartoon rain: ripples
  landing on the ground.` once a hole when the first ones land.

| # | Check | How | Pass if |
|---|---|---|---|
| C11 | Clear zone | Cartoon rain on Timber hole 2 as in the screenshot; look round, at a wide field of view too | No long slashes at the edges; streaks nearly parallel, slanting one way; still clearly rain, and still heavier-looking than `Rain = 1` |
| C12 | Ripples seen | Same place; stand still and look at the tee and the fairway | White two-ring ripples spread on the ground round you; the log line above appears |

**Changed (2026-11-20):** the ground ripples are taken out at the user's request (with the ripple picture in
`RainArt.cs`): cartoon rain is its streaks and its blue mist. C6 and C12 no longer apply; C2 no longer looks for
ripples.

**Results and changes (2026-11-20, screenshots of cartoon rain and rain on Timber hole 1):** snow was signed off
("looks good"). Cartoon rain reads as rain at once and no slash crosses the player or the tee: **C11 passes**. Two
problems:

- **Fat grey ovals in cartoon rain:** a few drops, at the left edge and over the water, drew as wide, faint ovals
  (about 25 × 80 px). They were the mid and far layers' big drops (0.06–0.08 m and 0.22–0.28 m) falling right past
  the camera: only the near layer had a clear zone, and the others' boxes still held the camera. Now every layer is
  a hollow cylinder nested inside the next, tilted along the fall: near 3–15 m, mid 15–45 m, far 45–120 m. Each layer
  keeps its density where it draws, so the look is the same with fewer particles: about 1,950 in all (was 2,445).
- **Rain nearly invisible:** at strength 1, a handful of faint lines; it read as a hazy clear day, and its splashes
  were a few pixels each. Its streaks are now a little wider (0.016–0.026 m, from 0.012–0.02; at most 0.5% of the
  screen, from 0.4%) and more opaque (0.5 near, 0.35 mid, 0.16 far; from 0.35, 0.25, 0.12), still thinner and
  greyer than cartoon rain's. Its layers are nested the same way (near 2–15 m, mid 15–45 m, far 45–120 m), so the
  stronger far drops can't fall past the lens: about 2,800 streaks (was 3,800), same look.

In both screenshots the top of the sky measured about as a clear day's. In the cartoon rain one the chat line shows
it had only just been switched on, inside the 10 s change; for the rain one the timing isn't known. To check (R1
and R2 below), screenshots taken 15 s or more after switching.

| # | Check | Steps | Expected |
|---|---|---|---|
| R1 | Rain | `Rain = 1` on a Forest hole by day; wait 15 s | Clearly rain: thin, grey-white streaks everywhere, still fainter and thinner than cartoon rain; the sky and fog grey; no wide ovals |
| R2 | Cartoon rain | `CartoonRain = 1`; wait 15 s; look round, at a wide field of view too | No fat grey ovals anywhere; streaks as before; the sky a soft blue mist |
| R3 | Frame rate | Rain and cartoon rain at 1 and 3, F8 both ways | No worse than before (fewer particles) |

**Results (2026-11-20):** the user checked rain and cartoon rain with this build: "Looks good." Snow, rain and
cartoon rain are signed off as they stand.


## HUD sizes

Asked for from two screenshots (walking and in the swing stance): every HUD group sized on its own, 0–100 %, 0 hiding
it; built into LightGreen as a `[HUD]` section, not a separate mod. The hole's beam and the aim line were taken off the
list at the user's request and are never changed. The user then asked for every other HUD part that could be scaled
cleanly as well: 27 settings in all, 28 with `Hints`, then 26 once the top-left corner became one (the README's table). **F8** gains a third step: on → HUD only (the game's own look
with the `[HUD]` sizes) → off (the game's own look and HUD) → on.

How it works (`Systems/HudScaler.cs`, `Systems/HudPatches.cs`, `Core/HudMath.cs`):

- **Screen elements** are found from the game's HUD components (`HoleProgressBarUi`,
  `WindInfoUi`, `Hotkeys`' inventory and swing groups, `SwingPowerBarUi`, `SwingPitchUi`, `RestartPrompt`,
  `ButtonPromptManager`'s corner and centre containers, `TextChatUi`, `InfoFeed`, `Scoreboard` and so on), and scaled
  just before the HUD is drawn each frame (`Canvas.willRenderCanvases`), toward the side each is anchored to. What the
  game set since the last frame is read back first, so its animations (the power meter's pop, the angle arc sliding to
  the centre) carry on at the new size. Elements that follow something on screen (crosshairs, lock-on markers, the
  angle arc, the giant-form timers) shrink about their own pivot.
- **Groups inside groups** (the stroke count and wind inside the progress bar, the swing wind inside the power
  meter): the outer group is scaled whole; the inner one is placed as if the outer one were at the game's size, then
  set against the outer one as scaled (see the results below for why it isn't split any more). **A container stretched over the screen** isn't
  scaled; its children are, each toward its own side.
- **Two wind readouts:** the higher one on screen is `Wind`, the lower one `SwingWind`.
- **Markers, name tags, pop-up words** are sized by the game every frame from their distance; Harmony postfixes scale
  them right after (`WorldspaceIconUi.OnPostPostLateBUpdate`, NameTagUi's `UpdateSize` local function,
  `TextPopupUi.UpdateSize`). A marker's group comes from the game's settings for its kind (hole, ball, teammate, or
  objective, ball dispenser and homing warning).
- **0 %** is a scale of 0.0001, not 0, so the game's own maths on an element never divides by zero.
- With `Chat` at 0, LightGreen's messages go to the top-left corner instead.
- **For this test build only**, `BepInEx/LightGreen.hud.txt` is written five seconds into the first hole of each game:
  every group's elements and the pieces scaled, with anchors, pivots and sizes, and the HUD's whole tree. It's for
  checking which element is which (the two wind readouts, whether the angle triangle and arc are separate, the power
  meter's container); it's taken out, or put behind a setting, before release.

Offline: `/tmp/hudtest` checks `HudMath` (percent to scale, 0 → 0.0001, which edge stays still, an element anchored
bottom right keeping its right edge at 50 % and shrinking into its corner at 0 %): all pass. The build compiles with no
warnings.

| # | Check | Steps | Expected |
|---|---|---|---|
| H1 | Log | Start the game | `[ok] Hook: HUD markers`, `[ok] Hook: HUD name tags`, `[ok] Hook: HUD pop-up words`; the load line says `F8 compares with the game's own look (on, HUD only, off)`; the config file has a `[HUD]` section of 26 settings at 100; no errors |
| H2 | Layout file | Play a hole for 10 s | `HUD layout written to …LightGreen.hud.txt` in the log; send the file (and `LogOutput.log`, for its `HUD: … is …` lines) |
| H3 | All at 50 | Every `[HUD]` setting at 50; walk, then take the swing stance (screenshot each, as before) | Every element from the two screenshots at half size, each at its own corner or side; nothing missing, nothing moved away from its place; the hole's beam and the aim line unchanged |
| H4 | All at 0 | Every `[HUD]` setting at 0 | All of them gone; the beam and the aim line still there; F8's message shows top left, not in the hidden chat |
| H5 | One at a time | Each setting to 0 alone, back to 100 before the next; most important: `ProgressBar` and `Strokes`, `Wind` and `SwingWind`, `ItemBar` and `AngleSelector`, `PowerMeter` | Only that group disappears |
| H6 | Live | Change a size in a config editor (ReConfig) while playing | It shows within a second |
| H7 | F8 | Sizes at 50; F8 three times | HUD only: the game's own look, HUD still at 50; off: the game's own look and its full-size HUD; on: both back. Chat: `on`, `HUD only (the game's own look)`, `off (the game's own look)`; F9, F10 and F11 work only at on |
| H8 | Animations | Sizes at 50: swing and release; change the angle; change items; take a stroke | The power meter pops, the angle arc slides to the middle and back, the slots grow when selected, the stroke count fades in and out, all at half size; nothing jumps or creeps across the screen |
| H9 | Other screens | Sizes at 50: Tab, quick chat, spectate after finishing, a golf cart, the next hole | Each at half size and in its place; the next hole keeps the sizes |
| H10 | Markers | `HoleMarker`, `BallMarker`, `NameTags`, `PopupText` at 50, then 0 | The markers and their arrows at the screen's edge, the name tags and the pop-up words at half size, then gone |
| H11 | Back | Everything back to 100; then `General.Enabled = false` with sizes at 50 | Exactly the game's HUD, in the same places |
| H12 | VR | Sizes at 50 with SBGVR | The HUD in the headset at half size, nothing stuck to the view |
| H13 | Frame rate | Sizes at 50 and 100, F8 between | No change in frame rate worth noting |

**Results and change (2026-11-21, first test of the HUD build, LogOutput.log from a session):** the user noticed hitching
with LightGreen on that the build before didn't have. LobbyEOS's frame statistics (every 10 s) in the same log show it:
while the `[HUD]` sizes applied (on, and HUD only) the worst frame of each 10 s was 20–37 ms against an average of
4.2 ms; after F8 to off (03:14:25, left there for the rest of the session) the worst frames were 5–12 ms, outside map
loads. The average didn't move, so the cost wasn't the per-frame scaling: it was the search for the HUD, run once a
second, 22 `FindObjectsByType(..., FindObjectsInactive.Include)` calls over the whole scene (about 89,000 Unity
objects), with a log string built for each element each time.

Changed: the HUD is found in one pass over the gameplay screen-space UI's own children (found by its
`GameplayScreenspaceUiCanvas` component, one search), only after a scene or hole loads (0.2, 1, 3 and 6 s after, while
the HUD is still building), when F8 or `General.Enabled` brings the sizes back, and when an element found before has
been destroyed (at most every 2 s). Never on a timer. The lock-on markers and giant-form timers, which come and go, are
the children of their managers (`Lock-on UI manager`, `Jumbo burger giant form timer manager`), looked at each frame.
Each element is logged once.

The same log answers part of the layout question (the `HUD: … is …` lines):

- `Strokes` and `Wind` are both inside the progress bar (`Hole progress bar/Container/Progress bar/Info/…`), and
  `SwingWind` inside the power meter (`Swing power bar/Wind info`): the outer groups are split round them, as planned.
- The angle arc (`SwingPitchUi`) is inside `Hotkeys/Swing`, so it had been folded into that group and scaled with it,
  toward the corner; it would then slide toward a point between the corner and the middle. Now it's its own piece,
  about its own pivot, and `Hotkeys/Swing` is split round it.
- The quick chat wheel's container stretches over the screen, so it had been taken apart and each option shrunk toward
  its own side. Now the wheel is scaled whole, about the middle of the screen.
- The item bar is `Hotkeys/Container`, the kill feed `Info feed/Message container`, the scoreboard
  `Scoreboard/Container`; the reticles each about their own pivot.

H13 (frame rate) is the check for this; H2's layout file still to send.


**Results and change (2026-11-21, H2 and H3: `LightGreen.hud.txt` from the Driving Range, and four screenshots with
every `[HUD]` setting at 50: Tab, walking, the swing stance, walking again):**

- Right at half size and in place: the hole and ball markers, `Hold R to restart` (half as wide, its bottom edge kept),
  the item bar with the angle badge and `Golf Club` (bottom-right corner kept), the `Swing Stance` and `Adjust Angle`
  prompts (bottom-left kept), the power meter (about its centre, by the player), the swing wind, the angle selector
  (bottom-right kept), and the scoreboard with its lobby name and course line (about the middle). The aim line and the
  hole's beam unchanged. The scoreboard came out half as tall but about 60 % as wide: the game's own width rule on it
  (`RectMaxWidth` on `Scoreboard/Container/Background`) gives it more room as it gets smaller; the rows and text are at
  half size.
- **Wrong: the progress bar.** The portraits and the flag shrank toward the top-left corner, but the green bar stayed
  at full length, so the flag sat in the middle of it. The layout file shows why: the bar's graphic is on
  `Hole progress bar/Container/Progress bar`, the parent of `Info`, which holds `Wind info` and `Strokes label`. Being
  split round those two, the bar was scaled through its other children (the flag, the portraits' parent) and its own
  graphic never.
- The layout file also shows `SwingPitchUi` isn't in the gameplay HUD at all: the angle selector is only
  `Hotkeys/Swing` (the triangle, its angle and the steps 1 to 4).

Changed: a group inside another is no longer split round. The outer element is scaled whole, its own graphics with it,
toward its still point; the inner one is worked out in its own parent as if the outer one were at the game's size, then
mapped against the outer one as scaled (position and scale divided back through the outer one's change). Outer
elements are scaled first each frame. Checked offline: 2,000 random cases of an outer element at 0.0001 to 0.9 and an
inner one at 0.0001 to 1, the inner one lands where and at the size wanted to within 1e-13. So `Strokes` and `Wind`
keep their own sizes and places at any `ProgressBar`, and can stay when the bar is hidden; likewise `SwingWind` with
`PowerMeter`. Two levels only (an inner group inside an inner group isn't in the HUD).

H3 again for the progress bar.

**Results and change (2026-11-21, H3 again, `ProgressBar` 50, `Strokes` 60, `Wind` 40, the rest 50):** the progress
bar now shrinks whole, bar, flag and portraits together in the top-left corner, and the stroke count and wind came out
at their own, different sizes: nested groups size independently. The rest as before; ReConfig's changes applied at
once. Two things changed after it, on the user's go-ahead:

- **The readouts followed nothing.** Placed as they'd sit under a full-size bar, the wind and stroke count stayed put
  while the bar shrank to the corner, about 110 px below it (likewise the swing wind beside the power meter). Now an
  inner group moves with its outer one and keeps only its own size: it's scaled about its own still point by its
  setting divided by the outer one's scale, so its still point goes where the outer one's scaling takes it. With the
  outer one hidden (0) there's nothing to follow, and it stays where it is with the outer one at the game's size (the
  mapping from before), rather than all of them gathering in the outer one's corner.
- **The tip pop-up wasn't covered** ("Cancel Swing: press F to cancel a charged swing", on the Driving Range): left out
  as tutorial-only, wrongly. A 28th setting, `Hints`, covers it (`TutorialPromptUi`) and the tutorial's objective box
  (`TutorialObjectiveUi`).

| # | Check | Steps | Expected |
|---|---|---|---|
| H14 | Readouts follow | `ProgressBar` 50, `Strokes` 60, `Wind` 40; then `ProgressBar` 0 | At 50: the wind and stroke count just under the half-size bar, each at its own size; at 0: the bar gone, the two readouts where they are with a full-size bar |
| H15 | Swing wind follows | `PowerMeter` 50, `SwingWind` 100, swing stance | The swing wind at full size, just under the half-size meter |
| H16 | Hints | `Hints` 50 on the Driving Range until a tip pops up | The tip box at half size |

**Results (2026-11-21, H14, two screenshots):** with `ProgressBar` 0 the bar is gone and the wind readout (40) stays
where it sits with a full-size bar, top left under where the bar was; with `ProgressBar` 50 it sits just under the
half-size bar, at its own size. **H14 passes for the wind.** The stroke count isn't on screen in either: the game shows
it only for a few seconds after a stroke (`HoleProgressBarUi`'s strokes visibility time), so its half of H14 still
wants a screenshot taken just after a stroke.

**Changed (2026-11-21, at the user's request): the top-left corner is one setting.** `Strokes` and `Wind` are gone;
`ProgressBar` now sizes the bar, the flag, the players' portraits, the wind readout and the stroke count together (the
two readouts sit inside the bar, so they simply scale with it). The two old keys are dropped from the config file on
load (`Settings.Removed`). The swing wind beside the power meter keeps its own setting (`SwingWind`), and the nested
handling stays for it. 26 `[HUD]` settings. H14 no longer applies; H3's top-left now means all of it at the one size.

**Results and change (2026-11-21, H15: `PowerMeter` 50, `SwingWind` 100, swing stance):** the meter at half size, the
swing wind at full size, and it followed the meter, but it overlapped the meter's bottom end. Its still point was its
own bottom-right corner (from its anchors, `a(1,0)` on the meter), so at full size beside a half-size meter it grew
upward into it. Changed: for a group inside another, the still point is the point of its rectangle nearest where it's
attached (its anchor point on the outer element, clamped into its own rectangle). For the swing wind that's just under
its top edge, below the meter's bottom-right corner: from the layout file (meter 51 × 422, wind 226 × 85, its top
4.5 units above the meter's bottom at the game's size), at `SwingWind` 100 with `PowerMeter` 50 its top now sits where
it does at the game's size, touching the meter's corner, and it grows downward. Screen-level groups keep the anchored
side as before. H15 again.

**Results (2026-11-21, H15 again):** the meter at half size, the swing wind at full size hanging just below the meter's
bottom end, clear of it, its badge under the meter's corner. **H15 passes.** H16 (`Hints`) remains.

**Results (2026-11-21, H16):** the user reports the tip pop-up at half size with `Hints` 50. **H16 passes.**

**Results (2026-11-21):** the user reports **all H checks pass** on build edf4d42 (H1–H13, H15, H16; H14 no longer
applies). The HUD sizes are done. Left before release: take out the layout file (`LightGreen.hud.txt`), a test-only
diagnostic.

**Changed (2026-11-21):** the layout file is taken out (`HudScaler.WriteLayout` and its tree dump); the DLL no longer
writes `BepInEx/LightGreen.hud.txt`. The `HUD: … is …` log lines stay, once per element. A `LightGreen.hud.txt` left
from the test builds can be deleted by hand. Nothing else changed: the build that passed the H checks, less the file.

## Hitching with many .cube filters (after 0.3.0)

**Report (2026-11-21):** slight hitching toward the end of a session, no crash; `LogOutput.log` (12:19–12:39 UTC) with
LightGreen 0.3.0, LobbyEOS, ReConfig, SliderSens and VCTextLobby. Found:

- `Photo filters: 27 in LightGreen.filters.json, 439 from .cube files`. Every `.cube` file was parsed at start and kept
  (`FilterStore.ReadFolder`), and LobbyEOS's memory lines show the managed heap at 319–321 MB in the menu (the session
  before, without the .cube files: 109 MB), 350–390 MB in play. Mono's collector goes over the whole heap each time,
  so a heap three times the size means longer pauses: 1–9 collections every 10 s in this log, more while ReConfig was
  saving the config.
- The worst frames at the end (40–70 ms, 12:38:43–12:39:23) line up with network stalls: the longest wait for a ping
  reply went to 2.7 and 3.5 s, the round trip's deviation to 299 ms and the time scale to 1.02 (catching up) as a
  client, then a map change (249 ms). Those aren't LightGreen's; the map-change frames (250–425 ms) are loading, as in
  every session.

Changed: `.cube` files are only listed at start (name → path). A filter's colour part (`FilterEntry.Spec`) is built
when it's first used, reading its `.cube` then, and the `.cube` of the filter used before is let go, so one is in
memory. A file that can't be read is logged when its filter is picked and the filter is used without it (the summary
line still counts every `.cube` file found).

| # | Check | Steps | Expected |
|---|---|---|---|
| L1 | Memory | With the 439 `.cube` files, start and wait in the menu for LobbyEOS's 30 s line | Managed heap near the 110 MB of before, not 320 MB; the log still says `439 from .cube files` |
| L2 | Filters still work | F11 through a few `.cube` filters, and back to one used before | Each shows as before; the first time each is picked can take a moment (it's read then) |
| L3 | A bad file | Put a broken `.cube` (a text file renamed) in the folder; pick it | A warning naming the file when picked; the picture unfiltered by it; nothing at start |
| L4 | Play | Play a few holes as before | No hitching beyond what the network does |

## Weather stutter and a growing hitch while hosting (after 0.3.0)

**Report (2026-11-21):** hosting a lobby (up to 19 players, 19:25–21:57 UTC, 70 map changes), "major frame stutters
with the weather effects on; with the weather off, only a consistent hitch". `LogOutput.log` with LightGreen 0.3.0
(the released DLL, not the L1–L4 test one: the heap was 333 MB in the menu and LobbyEOS's census showed
`LightGreen.Core.Rgb[]: 217 MB, 446`, the .cube tables), LobbyEOS, ReConfig, SliderSens and VCTextLobby. Found:

- `Materials tuned:` went from 315 toon materials at the first hole to 32,686 at the last, and LobbyEOS's census
  showed `LightGreen.Core.MaterialEdits.Stock` (with its four dictionaries) at 15,491, then 31,988. The game calls
  `Renderer.materials` for each player on each hole (`PlayerShaderDataHelper.SetPlayerShaderIndexOnRenderers`) and
  never destroys the copies; `MaterialTuner` found materials with `Resources.FindObjectsOfTypeAll<Material>()` and
  kept every one in `_known`, `_toon` and its stock table, and the biome went None and back in the same frame at
  each map change, so `Restore` never ran. Unity doesn't unload an asset a script still references.
- The consistent hitch: one frame over 50 ms every 20 s exactly (the scan's interval after a hole's first ten
  scans), growing from about 50 ms at 21:11 to 81 ms at 21:53. It doesn't follow the garbage collections.
- The weather stutter: in the 10 s windows while F10 stepped through the weather and for about 20 s after (the
  weather fading out), frames p95 33.5 ms against 4.5–5.6 ms otherwise. The shade colours follow the sky, so
  `MaterialTuner.Apply` re-set all 32,000 materials every frame of the blend.
- Not LightGreen's: `System.Byte[]` and `System.Int16[]` grew by about 50 MB an hour, and Mono's heap reached 625 MB;
  the map-change frames (300–650 ms) are loading, as in every session.

Changed: materials are found on renderers in the world (`FindObjectsByType<Renderer>`, inactive ones included,
`GetSharedMaterials` into one list), not every material in memory; at each map change `MaterialTuner.Forget` puts
them all back and lets go, and City night lighting does the same; later scans are every 10 s; a blend re-tunes at
most ten times a second. The `Materials tuned` line now gives the scan's time and renderer count, and a scan over
8 ms is logged once per map.

| # | Check | Steps | Expected |
|---|---|---|---|
| M1 | Counts stay level | Host or play for an hour or more; read the `Materials tuned` lines | The toon count is about the same on each map (hundreds to a few thousand), not climbing by hundreds each map; each line says the scan took a few ms |
| M2 | Weather | On a hole with 10+ players, F10 through rain, cartoon rain and snow, staying on each for a minute, then clear | No stutter while each fades in, stays or fades out |
| M3 | No regular hitch | Play with the weather off for 20+ minutes | No hitch every 10–20 s; LobbyEOS's `frames:` lines without a regular frame over 50 ms |
| M4 | Looks the same | Compare shadow tint on players, cosmetics and the course, water and snow glints, before and after a few map changes, and in City at night | As before; a newly put on cosmetic tinted within a few seconds |
| M5 | F8 | F8 through HUD only and off and back on a hole | Materials back to the game's in HUD only and off, tinted again when on |
| M6 | Memory | LobbyEOS's 30 s lines over the hour | `Unity allocated` and the process size don't climb as they did (1.06 → 1.87 GB and 4.3 → 9.2 GB in this log) |

Also from the heap paths of the same session (LobbyEOS, 19:37 UTC): `BallGlow._thisFrame` held a destroyed renderer.
The set is cleared at the start of each glowing frame, so when the glow stopped (daylight, F8, off) it kept the last
frame's balls, and the materials that can't glow (`_cantGlow`) were never let go. `BallGlow.Restore` now clears every
renderer and material list, and it runs at each map change too.

| # | Check | Steps | Expected |
|---|---|---|---|
| M7 | Ball glow | At night, see balls glow; change the time to day with F9, then back to night; play into the next map at night | Balls glow again each time; no ball keeps a glow by day |
| M8 | Heap paths | In LobbyEOS's heap paths after an hour or more | No `BallGlow.` or `MaterialTuner.` lines under "held through" (or only a few, from the current map) |

## Slight regular hitch (after the material fix)

**Report (2026-11-21):** "slight consistent hitching". `LogOutput.log` 00:41–00:45 UTC as a client (6 players, two
City maps, snow on the whole time) with the test DLL: the weather stutter is gone (p95 4.5–5.6 ms with snow), the heap
was 120 MB in the menu and the toon count 129 and 178. Every 10 s window's longest frame was 13–17 ms against a
4.2 ms median; the 0.3.0 log had the same, also in windows without a garbage collection, so part of it is the game's.
LightGreen's part: the material search every 10 s (3 s for a hole's first 30 s), 3.4–4.8 ms on 5,253–6,326
renderers in one frame; at night on City maps the window search every 15 s, which also made an array per renderer
(`sharedMaterials`); and ball glow at night made an array per ball per frame. A few 25–35 ms frames (about one a
minute) can't be placed from the log.

Changed: `RendererSweep` finds the renderers in one call and hands them out at most 0.5 ms a frame; the material
search and the City night search use it (materials found are tuned, and windows lit, when the sweep ends);
`GetSharedMaterials` into one list and `GetComponentsInChildren` into one list for ball glow. `WorkTiming` times each
feature, the HUD sizes and the per-frame key and config checks; frames with over 2 ms of LightGreen's work are logged
with their parts (at most once every 10 s), and with `General.TimingLog` the long frames (over 2.5 times the usual
frame and 6 ms more) with LightGreen's share of the longest.

| # | Check | Steps | Expected |
|---|---|---|---|
| T1 | Search | Load a City map; read the `Materials tuned` line | `looked through N renderers in X ms over F frames`, F above 1 (about X / 0.5) |
| T2 | Hitch | Play 5 minutes on one map | No hitch every 10 s; LobbyEOS's `frames:` windows' max lower than 13–17 ms, or the same with F8 off (T4) |
| T3 | Timing lines | With `General.TimingLog = true`, play a few minutes | `Timing (last 10 s): …` lines only when something was long; LightGreen's share of the long frames small (under 1 ms) |
| T4 | F8 off | Same map, 2 minutes on, then 2 minutes with F8 off, TimingLog on | The long frames with F8 off show what the game does alone; compare their count and size with on |
| T5 | Looks | City at night: windows light within a second of the hole starting; players and cosmetics tinted as before; balls glow at night | As before |
| T6 | Quiet log | Play with TimingLog off | `Timing` lines only around map changes (if any), not every 10 s |


## Crash: footprint gradients (memory dump, 2026-11-21)

**Report:** the game closed without warning at 00:55:55 UTC, 1 min into a Coast hole as a client (8 players, snow
weather on), with the T-test build (`89facd4`). Neither log has an error; Unity wrote no crash report. A full memory
dump (`Super Battle Golf.exe.19844.dmp`, 7.2 GB, Windows Error Reporting) was taken.

Found in the dump:

- `EXCEPTION_ACCESS_VIOLATION`, executing address 0, on `Job.Worker 4`. The stack word just below the stack pointer,
  where the last return address was, is 0, and the 0x1f0 bytes below it are zeroed: a function returned to a
  return address that had been overwritten.
- The live frames (from the return addresses that line up with each function's frame size): the job worker loop
  (`UnityPlayer+0x56c966`, `call r15` per index) → `+0x15338f0` (writes particle quad indices: index 35 of 36 in the
  batch) → `+0x1534050` → `+0xf59e40` → `+0xf7a8b0` (a 0x128-byte frame, its stack pointer exactly the crash's) →
  the function that returned to 0. The code round these is Unity's particle system (strings: "Unexpected Particle
  System vertex stream", "Precision issue while prewarming particle system"); Job.Worker 3 was in the same job.
- The particle system of that batch entry belongs to a GameObject named `Footprint Particle` (runtime instance id
  -142288). There are 12 `Footprint Particle` objects (10 active clones, 1 pooled, the prefab). The colour over
  lifetime gradient (the module's pointer at +0x840) is 2 colour and 3 alpha keys in the prefab and in 9 prints;
  in 3 it's garbage: 64/62, 0/0, and 120/115 keys in the one being built when it crashed. Unity's gradients hold at
  most 8 keys; 120 overran the job's stack.
- Unity 6's `MinMaxGradientBlittable.ToMinMaxGradient` wraps the module's native gradient in `new Gradient(IntPtr)`,
  whose constructor sets `m_RequiresNativeCleanup = false` (IL `16 7d ca 08 00 04`): a view of that system's own
  memory, not a copy (curves likewise, `MinMaxCurveBlittable.ToMinMaxCurve`). `Footprints.Capture` kept
  `colorOverLifetime.color`, `main.startColor` and `main.startLifetime` of the first print, and every footprint off
  snow was given them back (`fade.color = o.Fade`). Once that first print's gradient was freed, those reads were of
  freed memory.
- Gradient layout read from LightGreen's own fade gradient in the dump: 8 × RGBA float keys, 8 colour times and
  8 alpha times (UInt16), then the colour and alpha key counts.

Not related: RTSS (`RTSSHooks64.dll`) and the Steam and NVIDIA overlays are loaded, but no thread was in them.

Changed: `ParticleValues.Copy` makes gradients and curves LightGreen owns (`new Gradient()` + `SetKeys`, `new
AnimationCurve(keys)`), and `Capture` keeps those; `Configure` puts the game's values back only on prints it gave the
snow look (`Changed`), so off snow LightGreen doesn't touch prints at all. Also in this build: the `.cube` folder
check on a background thread every 2 s (`FilterStore.FolderChanged`), and the weather refill by `ParticleLayer.Fill`
(one `Emit` of the layer's steady count, then each particle given a random share of its life left) instead of
`Simulate` over a whole fall.

| # | Check | Steps | Expected |
|---|---|---|---|
| C1 | Prints off snow | Play two Coast or Grass holes with 8+ players walking about | No crash; prints look as without LightGreen |
| C2 | Prints on snow | A Snow hole: walk, wait 45 s, then walk off the snow onto other ground | Blue-grey prints that last and fade; prints off the snow the game's own |
| C3 | Map changes | Snow, then another biome, then Snow again | Prints right on each; no crash |
| C4 | Filter folder | Add, rename and delete a `.cube` in `LightGreen.filters` while playing | `Photo filters reloaded` within about 2 s each time; no `Keys and config` lines in `Timing` |
| C5 | Weather refill | Snow, rain and cartoon rain: start each, and spectate a far player | The view filled at once, no empty patch and no wave fading together; no `Snow`/`Rain` part over 3 ms in `Timing` lines |

**Results (2026-11-21):** C1–C5 passed. Released as 0.3.1.

## Audit fixes (after 0.3.1)

**Audit (2026-11-26):** a read-through of the whole source found no crash or broken feature, but 1 medium finding, 8 low
ones and 5 notes (the audit report). This build fixes or settles each:

- M1: `EnvironmentController.Darkness` counted the sun dimmed by the weather as dusk. It's split in two: `Darkness`
  (night only: the time of day's sun, or the game's own night holes) for ball glow, City lamps, windows and bloom;
  `SceneDarkness` (with the weather) for day and night brightness and the weather's own colours, as before.
- L1: the shadow settings stay as they were (never below the game's own, which the class is for); the READMEs say so.
- L2: `.cube` lines starting with a word are skipped as keywords unless the word is `NaN`, `Inf` or `Infinity`; NaN and
  infinite values are refused with their line; `Rgb.Clamp01(NaN)` is 0.
- L3: `MaterialTuner.Restore` lets go of the materials it looked at, and restarts a cut-short look, even with nothing tuned.
- L4: `BallGlow.Unglow` takes the block off a renderer that had none only while the block still holds the glow's values.
- L5: the City lamppost search goes through `ObjectSweep<Transform>` (the renderer sweep, made generic), at most 0.5 ms
  a frame. (The audit said the search wasn't timed; it was, as part of `City night`.)
- L6: footprints drop only the ids of destroyed prints, when the list passes 1,024 (then twice what's left).
- L7: the name-tag hook finds `<OnPostPostLateBUpdate>g__UpdateSize|…` by name without the compiler's number.
- L8: the grass-free depth is let go at `RenderPipelineManager.beginCameraRendering`, not kept for the frame.
- N1: `LocalRandom` for lightning and rain splashes (and `ParticleLayer`). N2: the camera-mode hook and `TimeLabel`
  removed. N3: copies of tuned materials found by name first; a value written over two stock values isn't used.
  N4: `tests/Tests.cs`, run by `build.ps1 -Test` (64 checks). N5: the manifest's `author` is kept (`build.ps1` names
  the package from it, and Thunderstore accepts it); a comment in `build.ps1` says so.

| # | Check | Steps | Expected |
|---|---|---|---|
| A1 | Rain by day | City hole at Noon, `Effects.Rain` 1 (F10) | No street lamps, lit windows or ball glow; the rain and the sky look as before |
| A2 | Rain at night | Same hole, F9 to Night, rain on | Lamps, windows and ball glow on, as without rain |
| A3 | Snow and cartoon rain by day | A Snow or Grass hole at Noon, F10 through snow and cartoon rain | No ball glow; the snow and the mist look as before |
| A4 | Lamp search | Night City hole; read the `City night:` line | `… lampposts lit … Looked through N renderers and M objects in F frames`, F above 1; no `City night` part over 2 ms in the log |
| A5 | Name tags | `HUD.NameTags` 50 in a lobby with other players | Log has `[ok] Hook: HUD name tags`; tags at half size |
| A6 | Ball glow off | Night, then F9 to Noon | Balls stop glowing and look as the game draws them |
| A7 | F8 and map changes | F8 through its three steps, then change map twice | Colours, shading and materials back to the game's at off; no errors |
| A8 | `.cube` errors | Put a `.cube` with a `NaN` line in `LightGreen.filters`, pick it | The log names the file, the line and the value; the filter isn't used |
| A9 | Shadows | `SunShadowResolution` 2048 with the game's Shadow quality at its highest | Log `Sun shadows on …` shows the game's size kept |

**Results (2026-11-26):** the user reports **A1–A9 pass** on build 8129aa1.

## Code quality fixes (after the audit fixes)

**Audit (2026-11-26):** a code quality audit found 3 medium findings, 11 low and 4 notes (the report). This build:

- M1: `PatchGuard` around every Harmony body (HUD markers, name tags, pop-ups, grass material, footprints, effect and
  lightning lights): the first failure is logged in full and the patch stands down. `WorldspaceIconUi.settings` is
  looked up without throwing.
- M2: `.cube` files are read and parsed on a worker thread (`FilterEntry.TryGetSpec`), line by line, colour lines in
  place; the filter before stays until the new one is ready. Size limits: `.cube` 96 MB, presets and filters 4 MB.
- M3: `IMapAware` (map and hole changes from `FeatureHost`); `WeatherSky` owns the overcast and runs first;
  `Weather.Start`/`Step` hold the one-weather rule and F10; `SkyLook` holds the sky material and ambient light.
- L1: silent catches log once (`CoreLog.FailedOnce`). L2: ordinal matching. L3: JSON depth 64; size caps. L4: whole-file
  writes (`SafeFile.WriteText`, `JsonFile`). L5: presets indexed per load, reused blend, ranked light sort, HUD
  children by index. L6: missing keys found once. L7: `JsonFile`, `TextureArt`, `FallingWeather`, `OwnWrite`.
  L8: `Footprints`, `EffectLights`, `LightningLights`, `GrassToggle` keep state on the instance. L9: full exceptions.
  L10: the footprint fade buffer grows to the print. L11: the checks run in a child PowerShell and the temp folder goes.
  N1: `CoreLog`, and `WorkTiming.LogLongFrames` set by the plugin. N2: `IsDefined(NonSerializedAttribute)`.
  N4: ball-glow log keys are `hole:` keys. N3: 86 offline checks (was 64).
- Not changed: the hole check at the top of each feature's `Apply` (each restores differently; a host-side check would
  hide that); `GrassFreeDepth` stays a static class (all of it is static, nothing mixed).

| # | Check | Steps | Expected |
|---|---|---|---|
| Q1 | Hooks | Start the game, read the hook lines | Every `[ok] Hook: …` as before; no `[FAILED]` |
| Q2 | Weather sky | F10 through rain, cartoon rain, snow and clear on one hole, then change hole with snow on | The sky greys, mists or turns pale blue and clears over about 10 s as before; on the new hole it starts overcast at once |
| Q3 | Weather keys | F10 a full round; set `Effects.Snow` 2 in the config while rain is on | One weather at a time; each comes back at its last strength; the config edit turns rain off and says `Weather: snow (2)` |
| Q4 | Big `.cube` | Put a 65-point `.cube` in `LightGreen.filters`, F11 onto it and off it a few times | No hitch on the press; the filter shows a frame or so later; `Timing` lines show no `Photo filter` part over 2 ms |
| Q5 | Map changes | Play two holes on one map, then change map twice (one City at night) | City lamps and windows, ball glow, footprints and materials right on each; `Biome:` lines, no errors |
| Q6 | F8 | F8 through on, HUD only, off and on, with weather and a filter on | Each step as before; off is the game's own look |
| Q7 | Looks | Each time of day with and without weather | Sky, ambient light and brightness as before |
| Q8 | HUD | `[HUD]` sizes at 50 | Markers, name tags and pop-ups at half size as before |

