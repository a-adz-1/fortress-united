# World materials

The futuristic look for maps, chosen by old texture name. Built surfaces
(walls, floors, ceilings, doors) get the procedural sci-fi panelling in
`Resources/SciFiSurface.shader`, which needs no textures; natural surfaces
(rock, ground) get CC0 PBR materials from the library below; lights and signs
glow; liquids get the water shader. `Game/MapAtmosphere.cs` sets the dusk
sky, fog and reflections.

```
python3 tools/materials/fetch.py          # 1K sets, ~330 MB download (--res 2K for ~1.1 GB)
python3 tools/materials/make_mapping.py   # after editing overrides.csv
```

- `library.csv`: the materials (CC0 sets from [ambientCG](https://ambientcg.com))
  and the real size one tile covers.
- `fetch.py`: downloads and packs them into
  `Assets/FortressUnited/Resources/WorldMaterials/` (not in git; cached in
  `~/.cache/fortress-united/materials`).
- `overrides.csv`: what each of the most-used textures is (a library material
  standing for its kind of surface), picked by looking at the textures.
- `make_mapping.py`: writes `Resources/MapMaterials.csv`, every texture in
  `docs/maps/texture-inventory.csv` with its look: `panel`, `metal`,
  `replace` (natural surfaces), `light`, `sign`, `glass` or `keep`, plus its
  team (team textures get team bands and glow).

The game reads the table in `Game/WorldMaterials.cs`; liquids (`!` textures)
get `Resources/Water.shader`. Without the fetched library the maps use their
original textures. To compare, record the look tour with and without:

```
DEMO_MODE=look DEMO_MAP=.../2fort.bsp DEMO_OUT=/tmp/after Unity -batchmode -projectPath . -executeMethod FortressUnited.EditorTools.DemoRecorder.Record
DEMO_MATERIALS=off DEMO_MODE=look ...
```

Credits: materials from ambientCG.com, licensed CC0 1.0.
