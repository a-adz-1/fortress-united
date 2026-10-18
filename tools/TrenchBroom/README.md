# TrenchBroom setup for Fortress United

`FortressUnited/` is a TrenchBroom game configuration: the game config, the
entity definitions (`FortressUnited.fgd`) and an icon.

The entity definitions use Team Fortress Classic's map format (goal
entities, keys, short aliases and flag bits exactly as TFC's game code reads
them; checked against the reconstructed TFC source), so TFC mappers'
knowledge and existing TFC maps carry over. Fortress United additions:
`worldspawn` key `fu_theme` (material theme for the importer) and
`func_spawnguard` (spawn protection volume).

## Install

1. Install TrenchBroom (`paru -S trenchbroom`, or the AppImage from
   trenchbroom.github.io).
2. Copy the `FortressUnited` folder into TrenchBroom's user games folder:
   ```bash
   mkdir -p ~/.TrenchBroom/games && cp -r tools/TrenchBroom/FortressUnited ~/.TrenchBroom/games/
   ```
3. Start TrenchBroom, pick **Fortress United**, and set the game path to a
   folder with your textures (WAD files), e.g. the TFC folder of a server.
4. Use the **Valve** map format (the only one offered).

## Status

Not yet load-tested in TrenchBroom (it isn't installed on the dev machine).
The config uses game-config version 8; if your TrenchBroom reports an error
loading it, copy the error text and it gets fixed. Maps saved from it are
read by `FortressUnited.Core.Maps.MapFile`.
