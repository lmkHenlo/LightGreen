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

