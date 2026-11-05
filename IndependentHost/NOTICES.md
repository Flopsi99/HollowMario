# Hollow Mario dependencies and credits

Hollow Mario's managed host and companion are built from this project's source.
The original Mario-in-Hollow-Knight mashup by Eliot Pacheco coto is credited as
inspiration; its bridge implementation is not a build or package input.
See IndependentHost/PROVENANCE.md in the source for the removal review and limits.

- libsm64, jaburns and contributors: https://github.com/libsm64/libsm64.
  Its public API is used by the host. The pinned native runtime is prepared on
  the player's PC, not shipped in public packages. Upstream licensing and the
  provenance of its decompilation/model inputs remain separate from our host.
- LibTessDotNet: https://github.com/speps/LibTessDotNet, SGI Free Software License
  B. Its notice is included as LIBTESS-LICENSE.txt or LICENSES/LibTessDotNet.txt.
- Harmony/HarmonyX, MonoMod and Mono.Cecil: their MIT notices accompany the
  packaged libraries. API HarmonyX is adapted to installed API dependencies;
  no retired bridge methods are retargeted or preserved.

Hollow Knight by Team Cherry and a supported player-owned Super Mario 64 ROM
from Nintendo are required. No ROM or Hollow Knight game assembly is included.
Mario textures, animation and audio are read from the player's ROM at runtime.
Private local test packages can contain a locally prepared native runtime and
must not be redistributed as public packages. No endorsement is implied.
