# Hollow Mario

Current local candidate: **0.10.121** (community API only).
Recent work covers Mario interactions, spells, arrivals, death/hazard recovery,
bench and map poses, web capture, creator unlock artwork/text, sound settings
and optional hitbox display. The Shaman knockout now also follows native FSM
phases, and the cell explicitly holds belly-prone before a belly get-up.
Later fixes cover native pit/room-exit priority, dark-room borders, the Soul
Sanctum forced fight, Birthplace presentation, and the trap bench reclaiming
Mario after the web escape. Focused checks do not replace gameplay verification.

Dreamshield receives one native attack reaction per damaging Mario move.
Grubberfly's Elegy uses native pooled beams on punches and kick strikes only;
slides, dives and pounds do not fire beams. Native health/Joni/Fury and Mark of
Pride beam rules apply. Quick Slash supports hit-confirmed punch/short-hop
recovery and adjustable air-kick recovery (default 0.2 seconds).
Steady Body becomes No Bonk Badge: a short wall-bonk recovery into a wall jump,
including after a dive, while retaining the Wall Jump upgrade requirement.
The creator has tested Grimmchild
and Weaversong successfully; this does not imply every charm combination passed.
F7 hitboxes and F8 boss HP start off; debug binds are editable in the API menu.
Ordinary builds disable developer logging and the camera/water file traces,
including when older saved settings requested them. Warnings/errors and test
reports stay available. Developer tracing requires an explicit diagnostics build.

Remaining release work: Melty recipe/package validation,
installed one-click test with Melty's locally prepared Mario runtime, and current
real gameplay media. Recent presentation changes still need in-game visual review.
Full Wings unfurl, other full cutscenes and stag travel remain out of scope;
the playable Wings move is already Mario's custom double jump.
The optional debug utility stays separate and must never enter a release.
No ROM, locally compiled Mario runtime, player settings or saves enter uploads.


Public Melty candidates use `Tools/Build-MeltyRelease.ps1` and the draft root
`melty.json`. The manual setup archive is a separate candidate. The Melty package excludes the compiled Mario runtime and models;
Melty prepares its declared library locally.

Melty's unattended test uses a test-only InControl pad to press/release Down,
observes the real Down action, and verifies normal bench exit before Mario's
movement/combat/switching/recovery checks. It does not force control flags or
take the desktop keyboard. The pad detaches and restores background-input state
after setup. A missing acknowledgement/bench exit fails the activation check.
All twelve declared results fit Melty's report limit; save/achievement writes
are suppressed only during a Melty test. Normal play does not create this pad.

`Build-HollowMario.cmd` builds and checks the community API target plus the
setup, then packages a local candidate. See [setup maintenance](Setup/MAINTAINING.md).

This project began with the moving safety-floor fix, inspired by **Mario 64 in Hollow Knight** by **Eliot Pacheco coto**. The community API build uses Hollow Mario's own host for native integration, rendering, audio, input and collisions. The original bridge and copied legacy converters are removed. See the [source and packaging review](IndependentHost/PROVENANCE.md) for retained upstream dependencies and the scope of that removal.

`Plugin.cs` contains startup and lifecycle wiring. Its concern-specific `Plugin.*.cs` files remain parts of the same runtime class, preserving hook identity and state ownership. Existing helpers are grouped under `Charms`, `Combat`, `Movement`, `Player`, `World`, `Presentation`, `Native` and `Diagnostics`.

The shared companion and replacement host are built together. Embedded artwork and generated definitions retain their existing locations; LibTessDotNet retains its license. There is no optional old-host target or DLL-retargeting build step.

Since .174, Mario is selected initially; M retains the normal character toggle.
Since 0.10.13, benches use a camera-facing Mario sitting/sleeping presentation
while the native bench sequence retains healing, saving, inventory and exit input.
The Knight's hidden animations continue supplying normal completion events.
Since 0.10.14, normal falling ledge grabs also check shallow platforms missed by
SM64's lower wall probe. Native grab reach/placement and collision geometry stay
unchanged. Movement.ThinPlatformLedgeGrabs disables this fallback next launch.
Since 0.10.15, camera fall tracking reads Mario's current descent instead of a
stale hidden-Knight falling flag. Landing releases the fall-follow latch before
camera smoothing, preserving normal look panning and room camera limits.
Since 0.10.16, crouching/crawling uses SM64's short-body proportion for the native
damage trigger, preserving its bottom/width and restoring it on standing. This
shares the existing pound-startup sizing; physical terrain clearance is unchanged.
Since 0.10.17, Mario's existing foot trigger also supplies real top-surface Player
contact to the native small/tutorial collapsing-floor handler. Native collapse
animations and persistence remain in charge; quake floors keep their requirements.
Since 0.10.18, enemy TAKE DAMAGE reactions follow confirmed Mario damage instead
of running through the earlier prop-event pass. Grimmkin can no longer cancel a
dash and withdraw their hurtbox before the dive's damage query reaches them.
Since 0.10.19, seated Mario is raised by 0.25 units and follows the Knight's native
bench tilt around his feet, including the Waterways bench. Only the private
bench actor's three snoring samples are suppressed; sleep/wake animation remains.
Since 0.10.20, bench wake visuals wait for the native inventory/pause exit check
to accept the request. The playable return starts facing the camera and blends
from the seat to the floor over 0.22 seconds without delaying player control.
Since 0.10.21, ordinary NPC Conversation Control look-up animations use a private
Mario looking toward the speaker. Native dialogue, choices, positioning and quest
events remain in charge. LookUpEnd lowers his gaze; other scripted hero poses
retain the Knight, and regaining control restores the regular playable Mario.
Since 0.10.22, the bench exit blend starts after the playable renderer's first
real draw and uses that draw's feet argument. Newly created, zero-centred mesh
bounds can no longer create a room-sized sideways offset on the first frame.
Since 0.10.23, the same interaction presentation begins before native corrective
walking, including NPCs, shops, stag tolls and inspect/tablet handlers. Mario uses
walking, idle, talk/read and background-facing poses while the native interaction
retains control. The last presentation mesh stays visible until gameplay's first
real draw, closing the Knight-flash gap at the end of an interaction.
Since 0.10.24, that presentation also follows native room/gate transitions:
walking sideways, rising/falling through vertical or airborne side exits, and
front/back-facing door walking. Native gates retain all movement, fades, timing
and destination rules; the new-game opening keeps its separate presentation.
Since 0.10.25, native casts use Mario presentation: side-facing punches for
Spirit, front-facing pound stages for Dive, and a front-facing triple-jump
celebration held at frame 12 for Wraiths. Upgraded casts share the shade-dash
tint. Native hazard recovery uses a fading lava reaction and the ROM lava voice;
its pooled Knight fade sprites are hidden only for that recovery and restored.
Spell costs, damage/effects, movement and hazard respawns remain native.
Since 0.10.26, native healing and crystal charging hold the early crouch pose at
frame 2. Crystal release/flight uses slide-kick, including continued room entries;
cancels and stops use crouch/slide recovery. Native focus/dash FSM states retain
visual ownership through charging, healing and long flights, releasing on their
real finish/cancel states. Healing, costs, charge time and flight remain native.
Since 0.10.27, hazard presentation hides only the verified root Knight sprite
on the acid/spike recovery prefabs, preserving their child splash/pierce sprites
alongside particles. Spell and crystal effects retain their native action flow.
Since 0.10.28, quick-map reading uses carrying idle/slow carrying walk, angled
15 degrees toward the camera from the side profile. Native Challenge Start/End
uses an angled back-facing punch held at full extension (frame 4), then idle.
Native map movement/UI and both Mantis Lords/Radiance challenge scripts retain
their control. No additional intro/outro/stag scene replacements are introduced.
F5 teleports to Mario's first grounded position after entering the current room,
held fixed through later landings and same-room handovers. It becomes available
after that first landing and does not reload enemies or use hazard checkpoints.
F9 retains the existing return-to-bench behavior. Current implementation and
verification limits are in [the .174 handover](Handover/ROOM-ENTRY-RESET-0.9.174.md).
Since .175, Mario's shadow recharge is two seconds from launch, independent of
landing; his half-second shadow phase is unchanged. See the
[recharge handover](Handover/SHADOW-RECHARGE-0.9.175.md).
Since .176, automatic out-of-bounds rescues share F5's fixed room-entry point and
teleport cleanup, including long-fall and Metal recovery; see the
[rescue handover](Handover/VOID-ROOM-ENTRY-0.9.176.md).
Since .177, an enemy actually damaged by a dive takes only a quarter of the final
damage from that dive's immediate landing slide, with normal successful-hit effects.
Fresh targets and unrelated slides retain normal damage; see the
[follow-up handover](Handover/DIVE-SLIDE-FOLLOWUP-0.9.177.md).
Since .178, accepted weakened follow-up hits use a short gray enemy flash through
the native flash system; ordinary hits retain their normal feedback. See the
[flash handover](Handover/WEAK-SLIDE-FLASH-0.9.178.md).
Since .179, equipped Quick Slash lets successful punches recover immediately on
the ground. Successful jump kicks return to neutral on landing, or return to an
actionable fall in the air. Since .181, air recovery waits 0.2 seconds from the
first damaging hit instead of the full animation. Another attack can then
be a kick at low speed or a dive at higher forward speed; ground pound remains
available. Since .182, a connected short-hop kick returns immediately to nearby
ground in neutral idle; it is distinguished by recent takeoff and height at the
start of the kick. Misses retain their normal commitment, and recovery does not refill
air moves or shadow recharge. Focused native-interface checks passed; live
gameplay remains unconfirmed. See the
[Quick Slash handover](Handover/QUICK-SLASH-0.9.179.md).
The .181 build includes the .180 correction to the edited charm-description
hook, targeting the inventory's actual text reader. This update is now installed
with the adjustable recovery setting at 0.2 seconds. See the
[timer handover](Handover/QUICK-SLASH-TIMER-0.9.181.md) and
[tuning guide](Quick-Slash-Tuning.md).

The accepted elevator and horizontal-conveyor source files are frozen; a folder move is not permission to change their behavior. See Setup/MAINTAINING.md for loader validation limits and release setup.
