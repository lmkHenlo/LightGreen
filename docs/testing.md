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
