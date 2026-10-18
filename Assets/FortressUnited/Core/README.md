# FortressUnited.Core

Game rules with no Unity dependency (`noEngineReferences` in the asmdef):
movement, classes, weapons, goals. Plain C#, so it can also be built and
tested with the .NET SDK outside the editor (see `tests/`). Unity code in
`Game/` and `Net/` wraps it.

- `Movement/`: player movement, ported to behave exactly like TFC's
  (pm_shared), checked against the faithful TFC build.
