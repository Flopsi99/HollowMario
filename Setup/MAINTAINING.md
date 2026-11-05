# Hollow Mario release setup

Hollow Mario supports the Hollow Knight community API only. Injection, the
Doorstop dispatcher, separate profile creation and injection-only menus have
been removed. Old installation names remain only for settings migration,
removal and rejection checks. The API runtime's BepInEx namespace is our small
settings/logging service contract; it never starts an injector or chainloader.

Tools/Build-Release.ps1 (or Build-HollowMario.cmd) builds the replacement host,
companion and setup, verifies dependencies, runs installer checks and packages
one API payload. Tools/Build-MeltyRelease.ps1 reuses that package and generates
the Melty candidate and root melty.json from the canonical automatic recipe.
Version numbers, manifests and archive names come from PluginVersion. Source
stamps include tracked and untracked source files. Neither builder publishes.

Ordinary builds are quiet: developer host events, camera/water file traces,
verbose gameplay logs and performance-hook installation are disabled even with
older saved debug settings. Essential errors and automated test reports remain.
Explicit local diagnostic builds use the MSBuild property
HollowMarioDiagnostics=true; no external crash-capture utility is packaged or
started by either build. Keep private captures out of source and release files.

Both launch workflows use the same setup executable. It verifies the supported
game version, obtains official checksum-pinned API v77 if missing, installs the
API payload, remembers the player's ROM and starts the original game. Manual
launch uses Steam; Melty launch starts that game's executable as a child so its
environment/test settings survive. No separate game profile is created. The
launcher does not downgrade games or change Steam branches.

Manual setup builds the native runtime using pinned upstream inputs and cached
tools. Melty declares libsm64 to its managed runtime folder and passes that path
to setup, which copies only sm64.dll into the mod's own runtime folder. A missing
prepared runtime stops setup. Public archives exclude that runtime, ROMs, game
assemblies, personal settings, saves, logs and the separate debug utility.
Retained third-party libraries keep their notices. No loader binaries or
Doorstop source archive are packaged.

Setup preserves other community mods and existing API settings. It imports
settings from our former injection install or this game's receipted private
profile when there are no current API settings. Every install/update removes
recognized older injection and API packages, disabled copies and obsolete
private profiles after settings migration and successful payload preparation.
Profiles containing other mods retain their game and those mods; ROMs and saves
are preserved. Cleanup keeps the current API package, preferences, download cache
and ROM handoff. It backs up and disables a known competing BepInEx injector,
and rejects unknown active injectors before installing the API. Old profiles
cannot be selected as the new game's installation.
The uninstaller still understands older installations and preserves ROMs,
game saves, other mods and shared loaders.

Setup checks cover a fresh API installation in a disposable copy of the player's
vanilla assembly, repeated install, package integrity, settings migration,
prepared runtime handoff, old-install removal and linked-path rejection. Native
and integration checks cover controlled gameplay contracts. These are offline
checks: live gameplay and Melty's installed-release test remain separate. The
recipe remains a draft until platform validation and that test pass. No game
is launched merely to verify a build or installation.

See IndependentHost/PROVENANCE.md for source provenance and its limits.
Historical Git commits and externally hosted old releases are not rewritten.
