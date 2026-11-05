Hollow Mario Debug Tools - separate community-API utility.

Pause a loaded save, then Options > Mods > Hollow Mario Debug Tools (LOCAL ONLY).
Choose upgrades/levels there. Charms support Not owned / Owned / Equipped.
Movement toggles include the cloak/claw permissions required by the native game.
Changes also refresh native attack damage and the health/charm display.
Kingsoul fragments are incomplete items; Void Heart stays automatically equipped.
Changes persist through normal room transitions, including scene-based bench warps.
Loading a save, returning to title, disabling the utility or choosing Restore
starting upgrades resets the overrides. No utility settings are written.

Before a save, overridden PlayerData fields (including dependent flags and charm
refresh changes) are restored on a separate save-data copy. Live debug selections
stay active. Other progress saves normally. This is not a disposable save sandbox:
collected objects, quests, map progress and cutscenes are not restored or replayed.
Keep a backup/test save when using this with other debug or warp tools.

Build DebugTools.csproj in Release. Install HollowMario.DebugTools.dll and this
README in Mods/HollowMarioDebugTools with the game closed. Replace the previous
utility copy so only one debug utility loads. Remove that folder to uninstall.
It does not depend on Hollow Mario's DLL. LocalTools is excluded from the main
mod build and release packages; this utility remains a separate local tool.
