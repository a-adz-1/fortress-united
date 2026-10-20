# Meshy prompts

Prompts for the models still to make, in the house style. Ask for the one
you need when you're ready.

## Rules for every model

- Generate the **blue** team version only. Red, gold and violet skins are
  made from it (`tools/meshy/recolour_team.py`).
- **No text and no logos.** Text comes out garbled; logos are added by the
  game (`Art/Logos`), so leave **one plain empty panel** where a logo goes.
- **Glass left clear or dark** (visors, screens, lenses) so the game can
  draw on it or make it glow.
- **No baked effects** (lightning, sparks, flames): the game adds them live.
- Team glows in one clear blue, so they can be split out and tinted.
- **Characters:** A-pose, facing forward, empty hands (weapons are separate
  models); same style as the medic (use the medic as the reference image if
  Meshy allows). Rig, then add animations; download FBX at 30 fps.
- **Things that move in parts** (sentries, the robot dog): clear gaps or
  round joints where they bend, posed straight, so they can be cut up.
- Every class needs one **signature feature** and its own **silhouette**:
  check it at thumbnail size before spending credits on rigging.
- No fixed team-like colours (teams are blue, red, gold and violet): the
  engineer's hazard stripes are in the team colour and black.

## Classes

| Class | Shape | Signature | Helmet |
|---|---|---|---|
| Scout | small, lean, long legs | jump thrusters on calves and back | streamlined, swept-back fins |
| Soldier | broad, square, upright | huge shoulder pads, missile rack | visor under a hard brim |
| Demoman | sturdy, stocky | bandolier of glowing canisters | round single cyclops eye |
| Heavy | short, extremely wide | giant chest, tiny head, power core | head sunk into the shoulders |
| Engineer | average, tool-heavy | hazard stripe plates, tool arm | hard hat |
| Spy | tall, thin, elegant | long coat-tails | smooth featureless head |
| Sniper | tall, lanky | cloak panels, scope eye | telescopic scope lens |
| Pyro | bulky, rounded | twin fuel tanks | sealed, heat vents |
| Civilian | four legs, low and long | robot dog with a melee arm on its back | face screen, antenna ears |
| Medic | done | crosses, repair kit | visor |

**Scout**
> Full body sci-fi robot scout character, small and lean athletic sprinter build, noticeably shorter and slimmer than a soldier, long legs and narrow shoulders, light minimal white and light grey armour plates on a dark grey flexible undersuit, large jump thruster boosters on the calves and a small twin-thruster pack on the back, streamlined helmet with swept-back fins and a narrow dark glass visor, glowing blue accent strips along the legs and thrusters, one plain empty panel on the chest, dynamic and fast looking, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Soldier**
> Full body sci-fi robot soldier character, broad square heavily armoured military build, average height, upright powerful stance, thick white and light grey armour plates on a dark grey undersuit, oversized angular shoulder pads, layered chest armour, backpack with a rack of small missile tubes visible over the shoulders, helmet with a wide dark glass visor under a hard protruding brim, glowing blue accent strips on the shoulders and missile rack, one plain empty panel on the chest, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Demoman** (canisters glow a neutral green-white, not team colour)
> Full body sci-fi robot demolitions expert character, sturdy stocky build slightly shorter than a soldier, white and light grey armour plates on a dark grey undersuit, thick bandolier of glowing cylindrical explosive canisters crossing the chest diagonally, more canisters on the belt, armoured blast collar around the neck, round helmet with a single large round dark glass cyclops eye, heavy reinforced boots, soft white-green light in the canisters, glowing blue accent strips on the gloves, one plain empty panel on the chest, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Heavy Weapons Guy**
> Full body sci-fi robot heavy gunner character, short and extremely wide stubby chunky build, enormous barrel chest and massive shoulders, thick short arms and short thick legs, tiny head sunk deep between the shoulders with a narrow dark glass visor slit, huge slab-like white and light grey armour plates on a dark grey undersuit, big glowing power core reactor on the back, armoured forearms like gauntlets, glowing blue accent strips on the reactor and forearms, one plain empty panel on the chest, looks like a walking tank, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Engineer** (stripes in the team colour, so blue and black here)
> Full body sci-fi robot engineer character, average build, white and light grey armour plates on a dark grey undersuit, bold hazard stripe armour plates in bright blue and black on the shoulders, knees and forearms, hard-hat style helmet with a dark glass visor, heavy utility tool belt with pouches and gadgets, backpack with a folded mechanical tool arm over one shoulder, glowing blue accent strips, one plain empty panel on the chest, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Spy**
> Full body sci-fi robot infiltrator spy character, tall slim elegant build, the thinnest of all classes, long limbs, smooth fitted white and light grey armour plates on a dark charcoal undersuit, long tailcoat-like armour panels hanging from the back of the waist, high armoured collar, smooth featureless egg-shaped helmet with a thin dark glass visor band, minimal clean refined lines, faint glowing blue accent strips, one plain empty panel on the chest, mysterious and sleek, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Sniper**
> Full body sci-fi robot sniper character, tall lanky build with long thin arms and legs, white and light grey armour plates on a dark grey undersuit, tattered camouflage cloak panels hanging from the shoulders and back, helmet with one large telescopic scope lens instead of a visor, smaller sensor lens beside it, slim backpack with an antenna, glowing blue accent strips along the scope and cloak edges, one plain empty panel on the chest, patient and precise looking, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Pyro**
> Full body sci-fi robot pyro character, bulky rounded build, average height, thick padded heat-resistant white and light grey armour on a dark grey sealed undersuit, large twin fuel tank backpack with pipes running to the right arm, fully sealed round helmet with a dark glass faceplate and heat vent grilles on the sides, chunky insulated gloves and boots, glowing blue accent strips on the fuel tanks, one plain empty panel on the chest, A-pose, facing forward, empty hands, no weapons, no text, no logos, plain grey background

**Civilian: robot dog VIP** (not rigged by Meshy: cut into parts and animated in code; the melee arm's tip is the shock prod)
> Large friendly sci-fi robot dog, mech hound about waist height, chunky segmented armour body in white and light grey on dark grey mechanical joints, a jointed robotic arm mounted on its back with a two-pronged electric prod tip, the arm folded neatly along the back, clearly separate leg and arm segments with visible round joints at shoulders, elbows, hips and knees, boxy head with a big dark glass face screen and two round antenna ears, short segmented robotic tail, glowing blue accent strips along the back and legs, one plain empty panel on each side of the body for a logo, standing straight on all four legs, cute and goofy but sturdy, no text, no logos, plain grey background

## Weapons and equipment

**Spy: killswitch hacking spike** (backstab = jack into the maintenance port and shut the robot down)
> Futuristic sci-fi spy hacking spike "killswitch", slim stiletto-like data spike, sleek matte black handle with fine grip texture and a small connector collar, thin tapered tip made of translucent glowing glass with a circuit pattern inside, glowing blue accent line along the handle, elegant and minimal, no text, no logos, plain grey background

**Sentry guns** (done; for re-rolls): same design per level, plain dark glass on the sensor eye, no text, the gun on a visible side axle and a turntable ring between base and turret, nothing (cables, plates) bridging moving parts.

**Done:** medic, repair kit, plasma spanner, sentries 1-3, conc, frag, MIRV and bomblet, EMP, pipe bomb, shotgun, super shotgun, super nailgun, rocket launcher, rail pistol, shock prod, killswitch spike (the melee for everyone without their own, made from: "Futuristic sci-fi electric cattle prod melee weapon, ... no lightning, no sparks, no text, no logos, plain grey background").
