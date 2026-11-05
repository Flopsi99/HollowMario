# Community API adapter

Windows Hollow Knight 1.5.78.11833 / Modding API v77 is the only supported
loading route. The Lumafly application is not required: setup can obtain the
official API itself. There is no injection build or dispatcher.

`Tools/Build-ApiCompatibility.ps1` stages our public API payload through
`IndependentHost/Build-ApiExperiment.ps1 -SkipNative`. It does not install or
launch anything, read an original bridge DLL or retarget its methods. A private
local test build omits `-SkipNative` and prepares libsm64 on this PC.

ApiEntry loads `HollowMario.Host.Integration.dll`, starts its host, then starts
the shared companion. HollowMario.ApiRuntime implements settings, logging and
MonoBehaviour services. Its BepInEx namespace is a service contract, not a
loader/chainloader. The API build refuses a loaded real BepInEx assembly.

HarmonyX 2.5.2 is checksum-verified from its official NuGet package and adapted
to the installed API's MonoMod/Cecil identities. Its MIT notice accompanies it.
ApiArgumentAdapter supplies typed hook factories; other mods' hooks are untouched.
No original mashup library is a build input.

Settings live in this mod's settings folder. Setup migrates previous injection
settings before removing obsolete installs. Atomic saves use MoveFileExW for Unity Mono compatibility.
No personal settings, ROMs, game DLLs or compiled Mario engine enter releases.

ApiChecks and the replacement-host regression runner check hook registration,
settings persistence/migration, argument handling and dependency identities.
They do not compare retired method bodies and do not certify every other mod
or complete live gameplay parity. The release requires an installed test before advertising one-click verification.
