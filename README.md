# Hollow Mario

Play Hollow Knight as Mario, with Mario movement and combat adapted to Hallownest, charm effects, selectable caps, and Mario animations for many interactions. Single player; this is a work in progress.

## Download and play

### Play via Melty
The new community-API-only build has not been published on Melty yet.

### Play locally
Download **HollowMario-0.10.121-Windows.zip** from [Releases](https://github.com/Flopsi99/HollowMario/releases).

1. Extract the entire ZIP and open **HollowMario-Setup.exe**.
2. Setup uses the community API and installs the supported official API automatically if needed.
3. Select your Hollow Knight installation and your own supported Super Mario 64 US ROM.
4. Let setup finish, then play. Mario starts selected; **M** switches characters.

Requires Windows, the Steam version of Hollow Knight **1.5.78.11833**, and your own unmodified Super Mario 64 US ROM. The API route supports community API **v77**. Lumafly itself is optional.

**No ROM, original game assemblies, compiled Mario engine or model is included.** First setup downloads verified build tools and pinned public source and prepares the Mario engine on your PC. This needs internet access and can take a few minutes. Textures, animation and audio come from your own ROM. Your ROM stays on your PC.

API setup preserves other mods and settings, but compatibility with every mod combination is not guaranteed. To update, extract the complete new release and rerun setup. Each install/update migrates settings and removes recognized old Hollow Mario injection builds, old API copies and obsolete private profiles while preserving ROMs, saves and other mods. Setup also offers removal.

You can instead copy the contents of `payloads/api` into `hollow_knight_Data/Managed/Mods/HollowMario`. On first launch, its setup helper prepares the missing local runtime and ROM location before starting. Use the installer to clean up an older installation automatically.

## Controls and settings

Cap selection uses the configured Cap Selection binding plus a direction. API bindings and Mario volume are in **Options > Mods > Hollow Mario**.

- **F5:** return to this room's first landing point.
- **F7:** toggle hitbox display, initially hidden.
- **F8:** toggle boss HP display, initially hidden.
- **F9:** return to the last bench.

Debug keys can be rebound in the mod menu. This prerelease disables developer logging and camera/water trace files, including retained debug settings. The separate developer debug mod is not included.

## Credits

Hollow Mario adaptations and artwork: Flopsi99. Inspired by Mario-in-Hollow-Knight by Eliot Pacheco coto; its bridge implementation is not included. libsm64: jaburns and contributors. Mario 64 Survivor adaptations and LibTessDotNet: their contributors. Loading support uses the Hollow Knight community API, Harmony/HarmonyX, MonoMod and Mono.Cecil; third-party notices accompany the release.

Hollow Knight is by Team Cherry; Super Mario 64 is by Nintendo. This fan mod is not affiliated with either company. Upstream license notices do not grant rights to the original games or assets.

## Building

With the .NET SDK, your own supported game and community API v77 installed, run `Tools/Build-Release.ps1 -GameDirectory "your Hollow Knight folder"` in Windows PowerShell. It prepares its API dependencies locally, builds the community API target and the setup, and runs the release checks. Build outputs and downloaded native sources stay outside the public repository. See `Setup/MAINTAINING.md` for installation, upgrade cleanup and validation limits.
