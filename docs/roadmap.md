# Roadmap

1. **Movement core (done: matches the original on all scenarios).** Port TFC player movement to plain C# in
   `Assets/FortressUnited/Core/Movement`, with a differential test harness
   that runs the same inputs through the faithful TFC build and through ours
   and compares positions and velocities tick by tick. Needs the .NET SDK
   for the tests.
2. **Greybox and a character controller (first version done: real maps with textures, TFC movement on the maps' collision hulls, keyboard/mouse).** A Unity scene with one map
   imported as boxes, a first-person controller driving the movement core,
   Input System bindings.
3. **Map pipeline (started: BSP/.map readers, texture inventory, TrenchBroom config, the first material library (46 CC0 PBR sets) and mapping table, and animated water done; the futuristic restyle, lights from fixtures, team bases, screens, neon signs and structural detail; the mapper framework (worldspawn settings, fu_ entities and textures, docs/mapping-guide.md); next more fu_ entities (lights with shadows, probes, post, particles, props), baked lighting, then the Unity importer and overlays).** TrenchBroom config + FGD for the goal entities; `.map`
   / `.bsp` importer; texture inventory of the user's maps and the first
   material mapping table.
4. **Networking.** Choose the netcode (Netcode for Entities / for
   GameObjects, or a third-party library), server-authoritative movement
   with client prediction using the movement core.
5. **One class end to end.** Soldier or scout: weapons, damage, death and
   respawn, HUD.
6. **Server extension points.** Event bus, settings, votes, HUD API (see
   `docs/design.md`); then the first modules: flag timer, headshots,
   airshots, map voting.
7. **All nine classes, flags and the goal system, buildings.**
8. **Art pass and water.**
9. **Bots, stats output, admin API.**
