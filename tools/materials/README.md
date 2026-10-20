# World materials

Modern PBR materials for maps, chosen by old texture name.

```
python3 tools/materials/fetch.py          # 1K sets, ~330 MB download (--res 2K for ~1.1 GB)
python3 tools/materials/make_mapping.py   # after editing overrides.csv
```

- `library.csv`: the materials (CC0 sets from [ambientCG](https://ambientcg.com))
  and the real size one tile covers.
- `fetch.py`: downloads and packs them into
  `Assets/FortressUnited/Resources/WorldMaterials/` (not in git; cached in
  `~/.cache/fortress-united/materials`).
- `overrides.csv`: hand-picked material and mode per texture, from looking at
  the textures. Modes: `replace` (the material, tinted to the old texture's
  colour), `detail` (the old texture with the material's grain, relief and
  sheen laid over it, so stripes, panel lines and team markings stay) and
  `keep` (unchanged: signs, lights, glass, see-through textures).
- `make_mapping.py`: writes `Resources/MapMaterials.csv`, every texture in
  `docs/maps/texture-inventory.csv` with its override or a default for its
  category (detail mode, so unreviewed textures never look worse).

The game reads the table in `Game/WorldMaterials.cs`; liquids (`!` textures)
get `Resources/Water.shader`. Without the fetched library the maps use their
original textures. To compare, record the look tour with and without:

```
DEMO_MODE=look DEMO_MAP=.../2fort.bsp DEMO_OUT=/tmp/after Unity -batchmode -projectPath . -executeMethod FortressUnited.EditorTools.DemoRecorder.Record
DEMO_MATERIALS=off DEMO_MODE=look ...
```

Credits: materials from ambientCG.com, licensed CC0 1.0.
