# Movement scenarios

Each file is a small world plus a list of input commands. The reference
harness (tfc-decomp `tools/movetest`) runs them through the original TFC
movement code and writes `../expected/<name>.trace`; the C# tests run them
through `PlayerMovement` and compare tick by tick.

Keywords:
- `box minx miny minz maxx maxy maxz`: solid box
- `water ...`, `ladder ...`: water / ladder volume (same six numbers)
- `brush nx ny nz d ...`: solid convex brush from planes (inside: n.p <= d)
- `origin x y z`, `velocity x y z`, `maxspeed v`
- `physinfo key value` (e.g. `physinfo tfc 1`)
- `movevar name value` (gravity, airaccelerate, accelerate, friction, maxvelocity, stepsize)
- `cmd count msec pitch yaw roll forward side up buttons [dpitch dyaw droll]`:
  `count` commands of `msec` ms; angles change by the d-values each command.
  Buttons: 2 jump, 4 duck, 8 forward, 16 back, 512 left, 1024 right.
