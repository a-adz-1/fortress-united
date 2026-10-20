# Map art direction

Modern, beautiful and futuristic: the maps should not look like Half-Life
with better textures. The robots set the style (white and light grey armour,
dark undersuit, team-colour glow), and the maps match it.

## Now (in the game)

- Built surfaces: procedural sci-fi panelling (`Resources/SciFiSurface.shader`):
  white ceramic walls, graphite floor plating with grip panels, lit ceiling
  strips, glowing seam lines; team textures get team bands with glowing seams.
- Natural surfaces (rock, ground): CC0 PBR materials, tinted to the map's colours.
- Water: animated ripples, reflections, sun glints; lava glows.
- Dusk sun and sky, fog, map reflections, ambient occlusion (`Game/MapAtmosphere.cs`).

## Ideas to add (references: Creepy Cat's 3D Scifi Kit on the Asset Store)

- **Moody interiors:** darker, warmer rooms lit by many small coloured lights
  (orange, red, green indicator dots) instead of flat ambient.
- **Real lights from the map's light textures:** a Unity point or area light
  at each light fixture face, in its colour, so rooms are lit by their fixtures.
- **Light strips** along floor edges, door frames and ceiling ribs (yellow,
  cyan, team colour).
- **Wall screens** with live UI (scores, flag status, class counts), using the
  same screen tech as the medkit's vitals display.
- **Structural detail:** curved ribs or arches along corridors, railings on
  walkways and ledges, pipes and cable runs, door frames, kick plates.
- **Signs as holograms:** the old signs redrawn as clean projected panels.
- **Team areas:** each base washed in its team's colour (lights, bands, glow),
  so you always know whose side you're on.
- **Per-map themes:** dusk (default), night (more glow), snow, desert, space.
- **Baked lighting and probes** per map for soft bounce light and reflections.

Asset Store packs can't go in this public repo (their licence forbids
redistributing the files); if used, they're kept out of git and each
developer imports them from their own account, like the CC0 library.
