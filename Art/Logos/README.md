# Team logos

Each team's logo is one vector file here, rendered to a white-on-transparent
PNG in `Assets/FortressUnited/Resources/Teams/Logo_<Team>.png`. Everything in
the game that shows a team logo (flag banners, decals on models) reads that
PNG, so changing a logo means replacing one file:

    rsvg-convert -w 1024 Art/Logos/team_red.svg -o Assets/FortressUnited/Resources/Teams/Logo_Red.png

- `team_red.svg`: the red emblem (spear, swept wings, ring), redrawn from the
  red flag concept.
- `team_blue.svg`: the blue emblem, the trident (option C).
- `blue_options/`: the candidate blue logos (A Aegis, B Crystal, C Trident,
  D Talon) and `options_sheet.png` comparing them with red.

Models should come from Meshy with logo areas left blank, so the game can
put the current logo there.
