# Models to remake

Every model Fortress United needs, grouped by role, with the TFC model it
replaces (for reference only: our models are new designs in the space/sci-fi
style). Tick an item when its game-ready model is in
`Assets/FortressUnited/Art/` and working in the game.

Status marks: `[x]` done, `[~]` in progress (note says what's left), `[ ]` to do.

Targets for game-ready models: FBX, real-world scale (players about 1.8 m),
PBR textures (base colour, normal, metallic, roughness; emission for anything
that glows, generated with Meshy's meshy-6 model), screens left as clear
glass (the game draws them). Rough triangle budgets: players 30-50k,
first-person weapons 20-40k, world weapons and pickups 5-15k, projectiles
1-3k.

## Player classes

Each needs blue and red team versions (or one model with team colour areas),
rigged as a humanoid, with run/jump/crouch/shoot animations.

- [ ] Scout (`player/scout`)
- [ ] Sniper (`player/sniper`)
- [ ] Soldier (`player/soldier`)
- [ ] Demoman (`player/demo`)
- [~] Medic (`player/medic`): blue and red rigged robots in
  `Art/Characters/Medic` (Humanoid, 20k triangles) with walk, walk
  backward, run, crouch walk, jump, swim, heal push and death; still
  wants idle, strafes, run backward, crouch idle and treading water
- [ ] Heavy Weapons Guy (`player/hvyweapon`)
- [ ] Pyro (`player/pyro`)
- [ ] Spy (`player/spy`)
- [ ] Engineer (`player/engineer`)
- [ ] Civilian (`player/civilian`)

## First-person weapons

What the player holds on screen (`v_` models). Each also needs a simpler
world version for other players to see (`p_` models), listed below.

- [x] Crowbar / axe, all classes (`v_tfc_crowbar`): replaced by the shock prod,
  `Art/Weapons/ShockProd`, team skins, zaps between the prongs on a hit
- [~] Spanner, engineer (`v_tfc_spanner`): blue and red plasma spanners in
  `Art/Spanner` (25k triangles, 2K textures) with a live plasma arc and a
  swing in the greybox; waiting on a first in-editor look
- [~] Repair kit (TFC's medikit), medic (`v_tfc_medkit`): blue and red models in
  `Art/Medkit` (30k triangles, 2K textures) with a live vitals screen in the
  greybox; waiting on a first in-editor look
- [ ] Knife, spy (`v_tfc_knife`)
- [x] Shotgun (`v_tfc_shotgun`): `Art/Weapons/Shotgun` (single blaster), red and blue skins
- [x] Super shotgun (`v_tfc_12gauge`): `Art/Weapons/SuperShotgun` (dual blaster)
- [ ] Nailgun (`v_tfc_nailgun`)
- [x] Super nailgun (`v_tfc_supernailgun`): `Art/Weapons/SuperNailgun`
- [ ] Sniper rifle / auto rifle (`v_tfc_sniper`)
- [x] Rocket launcher, soldier (`v_tfc_rpg`): `Art/Weapons/RocketLauncher`
- [ ] Incendiary cannon, pyro (`v_tfc_rpg`, own model in FU)
- [ ] Flamethrower, pyro (`v_flame`)
- [ ] Grenade / pipebomb launcher, demoman (`v_tfgl`)
- [ ] Assault cannon, HWGuy (`v_tfac`)
- [x] Railgun, engineer (`v_tfc_railgun`): `Art/Weapons/RailPistol`
- [ ] Tranquiliser gun, spy (`v_tfc_pistol`)
- [ ] Umbrella, civilian (`v_umbrella`)
- [ ] Hand holding a primed grenade (`v_grenade`)

## World weapons (seen in other players' hands)

Simplified versions of the first-person weapons (`p_` models).

- [ ] Crowbar / axe (`p_crowbar`)
- [ ] Spanner (`p_spanner`)
- [ ] Medikit (`p_medkit`)
- [ ] Knife (`p_knife`)
- [ ] Shotgun (`p_smallshotgun`)
- [ ] Super shotgun (`p_shotgun`)
- [ ] Nailgun (`p_nailgun`)
- [ ] Super nailgun (`p_snailgun`)
- [ ] Sniper rifle (`p_sniper`)
- [ ] Rocket launcher (`p_rpg`)
- [ ] Incendiary cannon (`p_srpg`)
- [ ] Flamethrower (`p_egon`)
- [ ] Grenade / pipebomb launcher (`p_glauncher`)
- [ ] Assault cannon (`p_mini`)
- [ ] Railgun / tranq gun (`p_9mmhandgun`, `p_spygun`)
- [ ] Umbrella (`p_umbrella`)
- [ ] Grenade in hand (`p_grenade`)

## Engineer buildings

- [x] Sentry gun level 1 (`sentry1`): `Art/Sentry`, cut into base / turret /
  gun / barrel / sensor, red and blue skins on one shape
- [x] Sentry gun level 2 (`sentry2`): twin barrels
- [x] Sentry gun level 3, with rockets (`sentry3`): gatling pods with
  spinning barrels, rocket pod
- [x] Sentry base (`base`): part of each sentry model
- [ ] Dispenser (`dispenser`)
- [ ] Teleporter entrance and exit (`teleporter`)

## Grenades and projectiles

- [x] Hand grenade (`w_grenade` / `grenade`): the frag, `Art/Grenades`, glow mask
  tinted per team; TFC's damage, falloff and knockback in the greybox (G)
- [x] Concussion grenade (`conc_grenade`): `Art/Grenades`, one model with a
  glow mask tinted per team; TFC's conc push and hand-conc in the greybox (F)
- [ ] Nail grenade (`ngrenade`)
- [x] MIRV grenade (`mirvgrenade`): `Art/Grenades`, red and blue skins on one
  shape, glow mask tinted per team
- [ ] Napalm grenade (`napalm`)
- [ ] Gas / hallucination grenade, spy (`spy_grenade`)
- [x] EMP grenade (`emp_grenade`): `Art/Grenades/EmpGrenade` (neutral, no team colour)
- [ ] Caltrops, scout (`caltrop`)
- [x] Pipebomb (`pipebomb`): `Art/Grenades/PipeBomb`, glow per team (bands) and
  type (window and end caps: green det pipes, blue launcher grenades)
- [ ] Detpack, demoman (`detpack`)
- [ ] Rocket, also used by the incendiary cannon (`rpgrocket`)
- [ ] Nail, also used by railgun and tranq darts (`nail`)

## Pickups and items

- [ ] Health pack (`w_medkit`): could reuse the medikit design
- [ ] Green armour (`g_armor`)
- [ ] Yellow armour (`y_armor`)
- [ ] Red armour (`r_armor`)
- [ ] Ammo backpack, dropped ammo (`backpack`)
- [ ] Ammo pickups: shells, nails, rockets, cells (`w_shotbox` and friends)
- [ ] Flag / goal item (`flag`): team versions
- [ ] Ball, soccer-style maps (`ball`)
- [ ] Key card (`keycard`)
- [ ] Quad damage (`quad`)
- [ ] Invisibility (`invis`)
- [ ] Invulnerability (`pent`)
- [ ] Environment suit (`w_suit`)
- [ ] Air tank / oxygen (`w_oxygen`)
- [ ] Antidote (`w_antidote`)
- [ ] Birthday presents, large and small (`presentlg`, `presentsm`): optional

## Effects models

- [ ] Shell casings: bullet and shotgun (`shell`, `shotgunshell`)
- [ ] Player gibs (`hgibs`, `agibs`): or replace with a stylised effect
- [ ] Breakable debris: glass, wood, metal, rock, concrete, computer, ceiling
  (`glassgibs`, `woodgibs`, `metalplategibs`, `rockgibs`, `cindergibs`,
  `computergibs`, `ceilinggibs`)

## Progress

| Group | Done | Total |
|---|---|---|
| Player classes | 0 | 10 |
| First-person weapons | 0 | 18 |
| World weapons | 0 | 17 |
| Engineer buildings | 0 | 6 |
| Grenades and projectiles | 0 | 12 |
| Pickups and items | 0 | 16 |
| Effects models | 0 | 3 |
| **Total** | **0** | **82** |
