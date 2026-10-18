# Fortress United

A free, community-run class-based team shooter for Unity 6, inspired by Team
Fortress Classic: TFC's movement, classes and flag game, with modern art,
community map tools and server features built in.

**Private repository** while the game takes shape.

## Ground rules

- **Free, always.** No paid features, cosmetics or early-access sales.
  Donations for servers are fine if they unlock nothing.
- **Our own name, art, sounds and maps.** Nothing of Valve's ships: no
  models, sounds, sprites, maps, logos or fonts. "Team Fortress" is Valve's
  trademark; describe the game as "inspired by Team Fortress Classic".
- **TFC is the spec, not the source.** The reconstructed TFC code (separate
  private repo `tfc-decomp`) tells us exactly how the original behaves. We
  port closely only where feel depends on it (movement, weapon and grenade
  timings, goal logic) and write everything else ourselves.
- **Every feel-critical system is tested against the original.** Movement
  and goal logic get differential tests: the same inputs through the
  faithful TFC build and through ours must give the same results.

## Layout

| Path | Contents |
|------|----------|
| `Assets/FortressUnited/Core/` | Game rules in plain C# with no Unity dependency (movement first). Testable without the editor. |
| `Assets/FortressUnited/Game/` | Unity-side gameplay (players, weapons, goals) built on Core. |
| `Assets/FortressUnited/Net/` | Networking. |
| `Assets/FortressUnited/Scenes`, `Art`, `Materials`, `Prefabs` | Unity content. |
| `Packages/manifest.json` | Unity packages: URP, Input System, UI. |
| `docs/` | Design, roadmap, Unity setup guide, map texture inventory. |
| `tools/TrenchBroom/` | TrenchBroom game config and entity definitions for mappers. |
| `tests/` | `Core/` (builds Core as .NET Standard 2.1 like Unity), `MovementTests/`, `MapTools/` (texture inventory, map reader checks). |

## Opening the project

1. Install Unity Hub and a Unity 6 editor (6000.x) with Linux build support:
   step-by-step guide in `docs/unity-setup.md`.
2. In Unity Hub: **Add > Add project from disk**, pick this folder.
3. The first open generates `ProjectSettings/` and `Library/` and may update
   package versions to match your editor; commit `ProjectSettings/` and the
   updated `Packages/` files afterwards.
4. Before adding art, install Git LFS (`git lfs install`) and add LFS rules
   for textures, models and audio, so the repository stays small.

## Tests

`tests/run-movement-tests.sh` builds the engine-free Core with the .NET 8 SDK
and runs the differential movement tests: each scenario in
`tests/MovementTests/scenarios/` is run through `PlayerMovement` and compared
tick by tick with the trace the original TFC movement code produced
(`tests/MovementTests/expected/`, recorded with tfc-decomp's
`tools/movetest`). To add a scenario, write the `.txt`, record its trace with
`build/movetest bin/legacy/tfc.so <scenario> > expected/<name>.trace` in
tfc-decomp, and run the tests.

## Status

- Movement core ported and matching the original on all 9 scenarios
  (walking, bunny hopping, stairs, ramps, crouching, water, ladders,
  falling, 1000/60 fps).
- Map pipeline started: `FortressUnited.Core.Maps` reads compiled GoldSrc
  maps (BSP v30: entities, face polygons, textures, texture axes, brush
  models, leaves) and `.map` sources (Valve 220 and classic formats, brush
  polygons). Checked on all 89 maps here (760,280 faces, no problems).
- Texture inventory of the user's 71 maps: `docs/maps/texture-inventory.md`
  (311 textures cover 90% of drawn surfaces).
- TrenchBroom game config with TFC-accurate entity definitions:
  `tools/TrenchBroom/`.
- Next: see `docs/roadmap.md`.
