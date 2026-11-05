# Hollow Mario host

This is the host used by the supported community API route. It is authored
against the pinned public libsm64 ABI and Hollow Knight's own interfaces. The
retired mashup DLL, copied legacy converters and DLL-retargeting builder are
removed. See [PROVENANCE.md](PROVENANCE.md) for the source review and its limits.

- Core owns native bindings, ROM validation, actor lifetimes and PCM buffering.
- Unity adapts the project-authored connected terrain converter and ROM geometry.
- Integration owns input, movement/control ownership, rendering, audio, collisions
  and shared companion contracts. `MarioHollow` type names and `MarioHollow-native`
  remain compatibility names for our implementation; its assembly identity
  is `HollowMario.Host.Integration`, never the retired `MarioHollow` assembly.
- The root companion owns gameplay adaptations, presentation, artwork and text.
  API services supply settings/logging and hook adaptation.

`Build-ApiExperiment.ps1` prepares a fresh local API test package by default.
Injection support and its build switches have been removed.
`-SkipNative` produces a distributable payload without the locally compiled
Mario runtime. Public manual/Melty packaging uses `Tools/Build-Release.ps1` and
`Tools/Build-MeltyRelease.ps1`. Neither consumes an old mashup binary.
The package verifier rejects the retired assembly, unexpected binaries, ROMs
and bundled host-game assemblies. Public packages also reject `sm64.dll`.

The player supplies Hollow Knight and a supported US ROM. Public packages use
player-local native preparation; Mario textures, animation and audio are read
from that ROM. No ROM, extracted Mario assets or game assemblies are embedded.
Retained upstream dependencies keep their licenses and notices.

`Run-Checks.ps1` runs native, integration and migrated regression checks without
opening a game or changing saves/input. See [RegressionChecks](RegressionChecks/README.md)
for what each layer proves. These establish source/package contracts and native
behavior, not complete visual or gameplay parity. Live gameplay verification
and Melty's installed-release test remain release gates.

Detailed diagnostics remain available for the creator's ongoing playtests.
The historical branch/script names do not select a second old runtime.
Git history and backups outside this working tree are not rewritten by cleanup.
