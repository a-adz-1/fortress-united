# Mapping for Fortress United

Fortress United plays Team Fortress Classic maps, and new maps are made the
same way TFC maps always were: build the level in a Half-Life map editor,
compile it to a `.bsp`, and drop it in. The game reads that map and turns it
into a modern, lit, sci-fi level by itself. You control how it looks with a
few settings, entities and textures, all placed in your editor; you never
need to open Unity.

- [How it works](#how-it-works)
- [Set up](#set-up)
- [Your first map](#your-first-map)
- [Textures](#textures)
- [Map settings](#map-settings)
- [Fortress United entities](#fortress-united-entities)
- [What the game adds by itself](#what-the-game-adds-by-itself)
- [Changing a compiled map](#changing-a-compiled-map)
- [Seeing your map in the game](#seeing-your-map-in-the-game)
- [Sizes and tips](#sizes-and-tips)
- [Coming next](#coming-next)

## How it works

There are two layers.

1. **Your map, from any Half-Life editor.** Geometry and entities, exactly as
   for TFC (the same gameplay entities, keys and flags). The game builds the
   look from them: every texture becomes a modern material, light fixtures
   become real lights, signs become neon, team bases take their team's colour,
   and corridors, ledges and doors get structural detail. Fortress United
   entities and `fu_` textures let you steer it.
2. **An optional Unity polish pass** (planned). The Unity importer turns the
   compiled map into a Unity scene that can be dressed further in Unity
   (baked lighting, extra props, animated set pieces), saved as an overlay
   that loads on top of the map. Gameplay geometry always stays in the map
   file, so movement plays exactly as you built it. Most maps won't need it.

Existing TFC maps work as they are, with no changes.

## Set up

**Editor.** Any of:

- **TrenchBroom** (free; Windows, Mac, Linux). A ready game configuration is
  in `tools/TrenchBroom/` (see its README to install it).
- **J.A.C.K.** or **Valve Hammer Editor 3.5**. Add
  `tools/TrenchBroom/FortressUnited/FortressUnited.fgd` as a game data file
  (it's in Hammer's own format), alongside TFC's.

**Textures.** Add `tools/maps/fortress_united.wad` (the `fu_` textures) to
your editor's texture list, plus TFC's and Half-Life's WADs if you want them.
The game ships with its own copy, so maps using `fu_` textures don't need to
include it.

**Compiler.** Compile as for any Half-Life map (VHLT v34 or similar:
`hlcsg`, `hlbsp`, `hlvis`, `hlrad`). `hlrad`'s lighting isn't used by
Fortress United (it lights the map itself), so a fast `hlrad` is fine while
you work.

**Units.** 1 map unit = 1 inch = 2.54 cm. A player is 72 units tall and 32
wide; a doorway of 64 x 112 units is comfortable.

## Your first map

Build it as a TFC map. The essentials:

- `info_player_teamspawn` for each team (with `team_no`). The game also uses
  spawn points to work out where each team's base is (see
  [team colours](#what-the-game-adds-by-itself)).
- Flags and capture points with `item_tfgoal` / `info_tfgoal`, as in TFC.
- `light_environment` for the sun's angle (its `pitch`, and `angles` for the
  direction).
- Light fixtures: give fixture faces a light texture (`fu_light_*`, or any
  TFC one with `~` in its name). Each becomes a real light in its colour.
- `info_tfdetect` for team settings, as in TFC.

Everything from TFC's entity reference applies; `FortressUnited.fgd` lists
it all with the same keys and flags.

## Textures

Every texture becomes a modern material, chosen by its name.

**TFC and other maps' textures** are handled automatically: about 1,900
known textures each have a look (built panelling for walls, floors and
ceilings; real materials for rock, ground and water; neon for signs; LED
panels for lights). Unknown names get a sensible guess.

**Fortress United textures** (`fortress_united.wad`) say exactly what you
want. The editor shows a preview; the game draws the real thing.

| Texture | Becomes |
|---|---|
| `fu_panel` | Sci-fi panelling: white ceramic on walls, graphite plating on floors, lit panels on ceilings (decided by the face's direction) |
| `fu_panel_dark` | The same, darker |
| `fu_metal`, `fu_trim` | Smaller dark metal panelling (doors, trims, machinery) |
| `fu_team_blue` / `_red` / `_gold` / `_violet` | Panelling painted with the team's bands and glowing team lines |
| `fu_light_white` / `_warm` / `_cool` / `_blue` / `_red` / `_amber` | An LED light panel that lights the room in that colour |
| `fu_glass`, `fu_glass_blue`, `fu_glass_red` | See-through tinted glass |
| `fu_logo_blue` / `_red` / `_gold` / `_violet` | The team's logo, glowing, fitted to the face |
| `fu_scr_blue` / `_red` / `_gold` / `_violet` | A live match screen (score, flags, clock) for that team, fitted to the face |
| `fu_rock`, `fu_cliff`, `fu_dirt`, `fu_grass`, `fu_sand`, `fu_snow`, `fu_gravel`, `fu_asphalt`, `fu_bricks`, `fu_wood` | Real (PBR) materials at real-world scale |
| `!fu_water`, `!fu_slime`, `!fu_lava` | Animated liquids (the `!` makes it a liquid volume, as in TFC) |
| `{fu_grate` | A see-through grate (the `{` makes the blue see-through, as in TFC) |

Tips:

- Panelling lines up with the world, not the texture, so texture alignment
  and scale on `fu_panel` faces don't matter.
- Logos, screens, light panels and signs stretch to fill their face: make
  the face the shape you want (a screen face 64 x 36 units looks right).
- For ordinary TFC textures the old naming rules still mean the same: `~`
  marks a light, `!` a liquid, `{` see-through, `sign...` a sign.

## Map settings

Keys on the `worldspawn` entity (the map's World entity properties). All are
optional.

| Key | Values | Default |
|---|---|---|
| `fu_restyle` | `1` the Fortress United look; `0` keep the map's own textures (lighting, signs, screens and detail still apply) | `1` |
| `fu_time` | `day`, `dusk`, `night` | `day` |
| `fu_sun` | `pitch yaw` in degrees, e.g. `50 135` (overrides `light_environment`) | from `light_environment` |
| `fu_suncolor` | `r g b`, 0-255 | daylight |
| `fu_fog` | fog density, `0` for none | `0.0025` |
| `fu_signs` | `neon` (signs re-lettered as neon), `hologram` (signs traced from their artwork) | `neon` |
| `fu_sky` | `auto` (the map's own sky, from `skyname`, when it's dark, e.g. space or night; otherwise the modern day sky), `map` (always the map's own), `procedural` (always the modern day sky) | `auto` |
| `fu_ribs`, `fu_rails`, `fu_frames`, `fu_screens` | `0` turns that automatic extra off | `1` |

A map already built in a clean modern style may look best with
`fu_restyle 0`, keeping its textures but getting the lighting and extras.
Under a dark sky (space), the game also switches to space lighting: no
haze, hard sunlight in the `light_environment` colour, and a moderate fill.

## Fortress United entities

**`fu_sign`** (point): a neon sign with your own wording.

| Key | |
|---|---|
| `text` | The wording. `\n` starts a new line; `->` draws an arrow. |
| `angles` | Its yaw is the direction the sign faces (out of the wall). |
| `height` | Height in units (default 32). The width follows the wording. |
| `team_no` | 1-4 for that team's colour. |
| `rendercolor` | Any colour instead (overrides the team colour). |
| `backing` | `1` a dark panel behind the letters (default), `0` letters straight on the wall. |

Place it a unit or two in front of the wall it's on.

**`fu_screen`** (point): a live match screen. Keys `angles` (facing),
`team_no`, `width` (units, default 64). Placing any `fu_screen` turns off the
automatic screens, so you choose them all.

**`fu_teamzone`** (brush): the volume is the team's base (`team_no`). Team
bands, glowing team lines, tinted lights and that team's screens follow it
exactly, instead of the game's guess from spawn points. Use it for bases that
aren't simply "around the spawns", or for more than two teams.

**`fu_norail`** (brush): no automatic railings inside it (e.g. a ledge
players should jump from).

**`fu_nodetail`** (brush): no automatic railings, ribs or door frames inside.

Volumes are invisible and don't block anything; build them from any texture
(the editor shows them see-through).

## What the game adds by itself

- **Lights.** Every light fixture face (light textures) becomes a real light
  in the fixture's colour, out in the room in front of it; `light`,
  `light_spot` and `env_glow` entities too. Rooms are lit by their fixtures,
  so a room without any is dark.
- **Team colours.** Each team's base is found from its spawn points (or your
  `fu_teamzone`s). Walls there get team bands with a glowing line, and white
  lights lean to the team colour. Ground between bases stays neutral.
- **Wall screens.** Up to four per base, on big, flat, indoor walls with open
  floor in front, at eye height.
- **Corridor ribs.** Arched frames with light strips every 3 m along
  corridors (walls on both sides 1.8-5.5 m apart, a ceiling 2.2-5 m up).
- **Railings.** Floor edges over a drop of 2.5 m or more get a glowing lip;
  under a roof they also get a rail. Open-air ledges (balconies, battlements)
  keep only the lip, so they read as open to jump from. Nothing collides.
- **Door frames.** Frames with glowing edges round every `func_door`
  (doors split into halves get one frame).
- **Sky, fog and reflections**, from the map settings.

None of it changes collision or movement.

## Changing a compiled map

`tools/maps/bsp_entities.py` edits a `.bsp` without recompiling: settings,
entities and texture names. Useful to try settings quickly, or to bring an
old map whose source is lost into Fortress United (with its author's
permission).

```bash
python3 tools/maps/bsp_entities.py mymap.bsp mymap_fu.bsp set-world fu_time dusk fu_signs neon
python3 tools/maps/bsp_entities.py mymap.bsp mymap_fu.bsp add fu_sign origin "100 200 -50" angles "0 90 0" text "FLAG ROOM ->" team_no 1
python3 tools/maps/bsp_entities.py mymap.bsp mymap_fu.bsp rename-texture sign_capt fu_scr_red
python3 tools/maps/bsp_entities.py mymap.bsp find-texture sign_team1
```

`find-texture` prints where faces with a texture are, to place entities next
to them. New brush volumes need the map recompiled; `rename-class` can turn
an existing brush entity (say a `func_nobuild`) into a `fu_norail`.

## Seeing your map in the game

In Unity: **Fortress United > Open Map...** and press Play to walk it.

To record a tour of it (wide views, each base, screens, signs, water) to
check the look:

```bash
DEMO_MODE=look DEMO_MAP=/path/to/mymap.bsp DEMO_OUT=/tmp/tour Unity -batchmode -projectPath . -executeMethod FortressUnited.EditorTools.DemoRecorder.Record
```

`DEMO_MATERIALS=off` records it with its original textures and lighting
(before/after); `DEMO_MATERIALS=keep` with its own textures and the new
lighting.

## Sizes and tips

- Corridors 96-200 units wide and 112-180 tall get ribs.
- Leave a flat, indoor wall at least 72 x 88 units with open floor in front
  in each base if you want automatic screens there.
- Light fixtures: several smaller fixtures light a room more evenly than one
  big one. Very large fixture faces are merged into one light per 2 m.
- Team signs: TFC's team sign textures (`sign_team1`...) and common signs
  (`sign_flag`, `sign_resup1`, arrows, exits...) are re-lettered as neon
  automatically; for anything else use `fu_sign`.
- Keep gameplay first: the look never changes collision, so test movement
  as you would in TFC.

## Coming next

More entities on the same pattern: `fu_light` (lights with shadows,
spotlights, area lights), `fu_reflection` (reflection probes), `fu_post`
(per-area colour grading and bloom), `fu_particles` (steam, sparks,
holograms), `fu_prop` (models from the Fortress United prop library by
name), `fu_sound` (ambient sound); more themes for `fu_time`; and the Unity
importer and overlays.
