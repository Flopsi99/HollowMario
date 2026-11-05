HOLLOW MARIO - COMMUNITY API VARIANT

Supports Windows Hollow Knight 1.5.78.11833 with community API v77.
The Lumafly manager application is not required at runtime.

Options > Mods > Hollow Mario contains native keyboard/controller rebinding
for cap selection and keyboard/mouse rebinding for the three debug commands:
room reset (F5), enemy health display (F8), and bench return (F9). Changes save
immediately. Enemy health starts hidden; its shortcut toggles this session.
Hollow Mario now uses the community API exclusively. Its launcher installs
official API v77 automatically on a supported vanilla game before starting it.
Injection and separate game profiles are no longer supported. Existing settings
are imported when possible; the uninstaller still handles old installations.

Melty prepares the native Mario runtime locally; it is absent from the public
archive. This preparation uses pinned public decompilation sources for code
and model geometry. The player's own supported US ROM remains required for
textures, animation and audio. No ROM, game assembly or compiled native engine
belongs in the upload.

The Setup subfolder is the ROM-only fallback helper, not another loader.
Normal Melty Play supplies the ROM location automatically. Existing settings
are retained. Source/manual installation is a separate developer workflow
which must also prepare its runtime locally; these managed DLLs alone do not
create it. Do not redistribute a prepared sm64.dll in a friend archive.

The API entry creates Hollow Mario's replacement host then the shared companion.
HollowMario.ApiRuntime provides settings, logging and component services, not
a BepInEx chainloader. No original mashup DLL or retargeted bridge is included.

No live disable/unload support is advertised; restart after changing mods.
Compatibility with other mods and all charm combinations remains an ongoing
test effort. A shared loader is not proof of gameplay compatibility.

Inspiration: Mario-in-Hollow-Knight by Eliot Pacheco coto.
libsm64: jaburns and contributors. Original upstream notices accompany it.
HarmonyX: Andreas Pardeike, Geoffrey Horsington, ManlyMarco and contributors;
see HARMONY-LICENSE.txt.
