# Design

## Look

Stylised PBR in the spirit of Unity's FPS Sample: chunky readable shapes,
strong palettes, atmospheric fog, dense dressing on walls and backgrounds,
clean floors and sightlines where fights happen. Team colours red and blue
must always read. Renderer: URP for reach and frame rate, with optional
higher-quality settings (fog, foliage density). Water gets special care:
stylised, colourful, with foam, refraction, caustics and an underwater
effect.

## Maps

Mappers keep the tools they know:

- **Editor:** TrenchBroom with a Fortress United game config and entity
  definitions (FGD) for the goal system (flags, capture points, goal groups,
  spawn points, spawn protection volumes, water).
- **Importer:** `.map` (and the user's existing GoldSrc `.bsp` maps) into
  Unity scenes: brushes, entities and keyvalues, with water and triggers as
  volumes.
- **Materials by name:** a mapping table turns each old texture name into a
  modern material (by category: brick, metal, wood, rock, sand, water...)
  and a per-map theme decides the variant. Unmapped textures fall back to an
  upscaled original.
- **Lighting:** baked GI and probe volumes, reflection probes, decals,
  per-map post-processing.

## Gameplay rules

Ported closely from TFC (and tested against the faithful TFC build):
player movement (including bunny hopping, ramp and water physics), class
speeds/health/armour, weapon damage and timings, grenade timers, the goal
system (flags, captures, goal groups, resets).

Known TFC bugs we must not copy are listed with their causes in the
tfc-decomp repo (`docs/bugfixes.md` on the `improved` branch). Examples:
fractional health shown as 0 to the client, primed grenades tied to "is
alive" rather than to a life, teleports not re-checking at arrival, one-shot
triggers not reset between rounds.

## Server extension points

Everything server-side beyond the core game is a small module on these.
(Learned from the AMX plugins the user's TFC servers run: they exist because
TFC had no way in.)

1. **Event bus:** player connect/spawn/kill (weapon, hit group, airborne
   state), damage, flag pickup/drop/return/capture, goals, rounds, chat
   commands, map start/end.
2. **Settings registry:** typed server settings with description, range,
   default and per-map overrides.
3. **Vote framework:** questions, choices, pass ratio, runoff; used by map
   votes, rock-the-vote, mode votes and kick votes.
4. **HUD API:** team-coloured messages, announcer sounds, world-space bars
   and icons.
5. **Stats output:** structured JSON events for the web panel.
6. **Admin API:** authenticated interface for the web panel.
7. **Map entities for map logic:** e.g. spawn protection volumes instead of
   plugins with hard-coded coordinates.

## Planned server features

- Bunny hopping setting: off / TFC default (1.7x cap) / uncapped. The
  setting must reach clients so their movement prediction matches.
- Flag timer above dropped flags (off / team flags / all flags / all plus
  announcement).
- Headshots: off / sniper only (TFC default, 2x) / all hitscan, plus
  announcements.
- Airshot, mid-air and double-airshot announcements.
- Map voting: rock-the-vote, nominations, end-of-map vote with runoff,
  extensions, recent-map bans, player-count map pools.
- Game modes as settings bundles (e.g. competitive class limits), voteable.
- Built-in bots: per-map rosters and class priorities, flag handoff to
  humans, pass-through.
