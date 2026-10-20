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
- Dusk sun (at the map's own angle) and sky, fog, map reflections, ambient
  occlusion (`Game/MapAtmosphere.cs`).
- Real lights (`Game/MapLights.cs`): a point light at each light fixture (light
  textures, merged on a 2 m grid) in its colour, and at glow sprites and light
  entities; dim ambient, so rooms are lit by their fixtures.
- Team bases: found from each team's spawn points; walls there get team bands
  with glowing team lines, and white lights lean to the team's colour.
- Indicator lights: small blinking coloured dots on some panels.
- Wall screens (`Game/MapScreens.cs`): placed automatically on big, flat,
  indoor walls in each base; they show the match live from `MatchInfo`
  (logo, base name, clock, score, flag status, news ticker).
- Team logos: TFC's team emblem textures become our logos, glowing on a dark panel.
- Structural detail (`Game/MapDetail.cs`), from the map's own geometry, with no
  collision: arched ribs with light strips every 3 m along corridors; railings
  with a glowing lip and rail on floor edges over a drop; frames with glowing
  inner edges round doors. Glow strips take the team colour in a base.

## Ideas to add (references: Creepy Cat's 3D Scifi Kit on the Asset Store)

- **More screen content:** class counts, kill feed, sentry status, a map of the base.
- **More structure:** pipes and cable runs, kick plates, frames round open
  doorways (not just doors), ribs in wider halls.
- **Signs as holograms:** the old signs redrawn as clean projected panels.
- **Per-map themes:** dusk (default), night (more glow), snow, desert, space.
- **Baked lighting and probes** per map for soft bounce light and reflections.

Asset Store packs can't go in this public repo (their licence forbids
redistributing the files); if used, they're kept out of git and each
developer imports them from their own account, like the CC0 library.
