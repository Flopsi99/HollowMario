# Offline regression checks

`IndependentHost/Run-Checks.ps1` is the entry point. Supply `-RomPath` for the
player-owned supported ROM and `-Python` if Python is not on PATH. It uses the
latest successful independent API package, or an explicit `-PackageDirectory`.
`-BuildPackage` prepares a fresh private API package first. The game, game files,
saves, desktop and input devices are never opened or modified by these checks.
Build dependencies must already be restored; no tools are downloaded by this runner.

For a focused rerun, `-Only` accepts comma-separated names from `suites.json`.
It runs just those migrated regressions. Do not rerun everything for a wording
edit or imply a policy fixture verifies an actual Unity scene.

The full runner includes:

| Layer | What it establishes | What it does not establish |
| --- | --- | --- |
| Core native checks | ABI, real native floor/movement/geometry/audio, queue concurrency, actor lifetime and reinitialization | Unity rendering or gameplay feel |
| Integration native checks | Packaged companion hooks on the replacement; real simulation, moving/removed platforms, cinematic actors; 13 ledge collision scenarios | All Unity collider shapes or every room |
| `native` regressions | Actual packaged presentation actors and native exports, ROM animation geometry/audio, native No Bonk behavior | On-screen composition and live handovers |
| `fixture` regressions | Actual packaged hooks/metadata with controlled Unity endpoints or pure inputs; also the public native floor helper in NativeMetadata | End-to-end engine execution; see each log |
| `policy` regressions | Source-linked Quick Slash, dive/slide damage and shade timing with owned ABI/controller/Unity stand-ins | Host integration or actual enemy encounters |
| Room-entry / bench-return policy projects | Source-linked reset, void destination, recoil/invulnerability and F9 coordination with fakes | Actual room/bench transitions |

The original 69 migrated suites are listed explicitly in `suites.json`. Sixty historical
project fixture sources formerly existed only in ignored local artifacts; their
retargeted versions are now tracked in `Fixtures`. Other canonical sources remain
under `Tools` and are compiled directly. No fixture loads a previous companion
build, installed mod directory or retired bridge DLL. The common bootstrap checks
the loaded assembly locations, rejects the retired identity, and initializes our
host through its actual lifecycle entry. Internal presentation actors are invoked
from the package by a test-only reflection adapter, rather than compiling a second
copy of their implementation into the tests.

Every suite gets a separate process in Hollow Knight's installed Unity Mono.
This provides its runtime and Harmony dependencies without starting Unity, a
window or a save. Native globals and detours cannot leak into the next suite.
Compiler errors, exceptions, timeouts and crashes fail; no fallback to an old
package is permitted. At most two processes run concurrently.

Results and per-suite logs are local under
`.artifacts/independent-host/regressions`. `results.json` records the full migrated
run; `results-selected.json` records a focused rerun and does not overwrite the
full result. Reports include exact package/native/game assembly hashes and test
source hashes. `summary.json` records the full runner's other steps as well.
Counts refer to suites, not individual assertions or gameplay coverage.

Intentional expectation updates retained the creator's later behavior:

- Bench lift is now .32, rather than the earlier .25.
- Hazard return has a moving recovery into idle rather than a permanently held frame.
- Web escape retains Mario through landing and interpolates the short seated struggles.
- Head-return tails keep their existing presentation, including a late reacquisition.
- Healing is covered; crystal cancellation stays covered until native control returns.
- Dream Nail uses the Bowser-tail swing and Dreamgate uses the stationary twirl.
- Diagnostic displays/logging default off; recovery hotkeys remain enabled.
- Actor recycling belongs to our host; sound cleanup is still required at handover.

`superseded.json` maps two obsolete/duplicate fixtures to their current replacements.
The former method-body-preservation comparison has been replaced by dependency and
assembly-identity checks; it no longer opens or compares the retired implementation.
The historical loader-parity comparison is not evidence for this experiment: a
current community API release still requires its installed gameplay check.
Exploratory probes and old ignored binaries are not part of the supported suite.
The five tracked precompiled check executables were removed to prevent accidental
execution of stale tests; sources are built by the runner.

On 2026-11-02 the consolidated run passed all 68 suites present at that point,
plus the core, integration, room-entry and bench-return checks. The additional
BenchReturnMetadata suite passed separately against the same package. These are
offline results, not full feature parity or release readiness. Live rendering,
audio quality, collisions in real rooms and full control handovers still need
in-game verification when the creator authorizes it.

The 2026-11-03 playtest fixes add `DamageDeliveryChecks` (70 suites total),
actual red/blue/maskless/nested damage dispatch and active native protection,
DSP resampling/continuity checks, real kick and knockback simulation, repeated
pound startup/descent/landing after actor reuse, left/right side-flip-to-pound
geometry, and renderer conversion on ROM-generated vertices/eye decals.
Integration checks now run from an isolated executable directory and assert that
the host assembly actually loads from the delivered package; project-reference
copies cannot shadow it. Screen appearance, live collisions and listening still
need player verification. Existing passing offline tests were insufficient to
certify these behaviors before this playtest.

Final 2026-11-03 diagnostic package: all 70 suites plus the core/integration and
room-entry/bench-return policy checks passed. The exact verified package was
installed into the closed game's existing community API mod folder, with prior
files backed up and settings preserved. No game was launched. GPU appearance,
audio listening and actual collision/interaction behavior await the creator's
playtest; the save-load-only pound cancellation was not reproduced offline.

A subsequent live startup exposed an unsupported combined `__args`/`out __state`
damage prefix. The hotfix restores the state-only snapshot and captures source,
amount and hazard through a separate typed prefix. DamageDeliveryChecks now
registers the exact production snapshot/capture/guard/finalizer combination on
HeroController.TakeDamage through the actual API adapter, and executes typed
capture on an owned callback. That check, ApiChecks and NativeMetadata all passed
for the hotfix package. These are focused checks, not a new full 70-suite run.
