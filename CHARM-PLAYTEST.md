# Hollow Mario charm playtest list

Reviewed against the current 0.10.119 code on 5 November 2026.

This covers all 40 charm slots, including their alternative versions. It is a
playtest list, not a claim that every charm has passed in the replacement host.
The older compatibility notes still call Quick Slash, Elegy and Steady Body
missing; those statements have been superseded by the implementations below.

**Mario adaptation** means the project explicitly connects the effect to Mario.
**Native route** means Hollow Knight still supplies the effect through the
shared player, spells, focus or world systems; live confirmation is outstanding.
**Closer check** identifies effects whose full behaviour remains uncertain.

Start with Quick Slash, Strength, Fury, Heavy Blow, Sharp Shadow, Thorns,
Stalwart Shell, Dreamshield, Elegy and the cap-related charms. These cross the
replacement host's attack, damage, action or control boundaries most directly.
Then check the companions, clouds and regeneration effects.

For each charm, compare equipped and unequipped behaviour. Where relevant, also
try a room change and a return from healing, casting or a bench. A visible
effect alone does not confirm damage, SOUL, duration or protection.

| Done | Slot | Charm | Current route | What to check |
|---|---:|---|---|---|
| [ ] | 1 | Gathering Swarm | Native route; previously creator-confirmed | Loose Geo is collected while Mario moves; followers survive room changes. |
| [ ] | 2 | Wayward Compass | Native route; previously creator-confirmed | Map marker follows the correct room and position; closing the map returns control normally. |
| [ ] | 3 | Grubsong | Native route; previously creator-confirmed | Accepted damage gives SOUL; blocked damage does not. Gained SOUL must not replenish an active cap's fuel budget. |
| [ ] | 4 | Stalwart Shell | Native damage timing connected to Mario | Compare protection duration and its expiry, including bonks, get-ups, healing and room changes. |
| [ ] | 5 | Baldur Shell | Native focus handover | Protection blocks hits while focusing, uses up its shield correctly and ends when focus stops. |
| [ ] | 6 | Fury of the Fallen | Mario adaptation using native Fury multiplier | Physical damage rises at the correct health threshold and returns to normal after healing; compare Strength and blue-health/Joni combinations. |
| [ ] | 7 | Quick Focus | Native focus handover | Healing is faster, can be interrupted normally and returns to Mario correctly. |
| [ ] | 8 | Lifeblood Heart | Shared native health | Extra blue masks appear and are consumed correctly; check bench and room transitions. |
| [ ] | 9 | Lifeblood Core | Shared native health | Larger blue-health bonus, damage and reset behaviour remain correct. |
| [ ] | 10 | Defender's Crest | Native clouds retained | Continuous smell is visible and nearby enemies actually take damage at the expected cadence. Earlier feedback reported sparse emissions. |
| [ ] | 11 | Flukenest | Native spell handover | Fireball replacement spawns and damages correctly; compare Shaman Stone and Defender's Crest combinations. |
| [ ] | 12 | Thorns of Agony | Mario damage adaptation using native thorns | Accepted hits produce visible damaging thorns after the windup, without taking Mario's control. Check Stalwart Shell, Metal and interrupted/reset bursts. |
| [ ] | 13 | Mark of Pride | Mario adaptation | Physical reach increases by 25%; with Longnail the total is 40%. Test punches, kicks, dive, pound and stomp without enlarging incoming damage reach. |
| [ ] | 14 | No Bonk Badge (Steady Body slot) | Mario adaptation | With Wall Jump unlocked, recover a recent wall bonk or dive collision into a wall jump. Enemy/hazard recoil should not qualify. |
| [ ] | 15 | Heavy Blow | Mario adaptation using native recoil multiplier | Physical hits push movable enemies farther; compare recoil-immune enemies and ordinary versus Metal hits. Mario's own bounce should stay unchanged. |
| [ ] | 16 | Sharp Shadow | Mario adaptation | Only the active shadow part of a shaded long jump damages enemies. Ordinary long jumps and the part after shadow expires stay harmless. |
| [ ] | 17 | Spore Shroom | Closer check: native focus cloud | Cloud appears after healing, stays visible after Mario returns and damages nearby enemies; check Defender's Crest combination. |
| [ ] | 18 | Longnail | Mario adaptation | Physical reach increases by 15%; compare Mark of Pride and their additive combination across the same move families. |
| [ ] | 19 | Shaman Stone | Native spell handover | Actual spell damage and size increase; check fireball, dive spell, scream and Flukenest. |
| [ ] | 20 | Soul Catcher | Native hit reward plus Metal adaptation; ordinary gain previously creator-confirmed | Successful attacks gain extra SOUL. Metal's successful-hit cost falls from 5 to 3, without reducing passive upkeep or incoming-hit cost. |
| [ ] | 21 | Soul Eater | Native hit reward plus Metal adaptation | Successful attacks gain its larger SOUL bonus, including reserves and Soul Catcher stacking. Metal's successful-hit cost is 1, including when both are equipped. |
| [ ] | 22 | Glowing Womb | Closer check: native companion | Hatchlings spawn, follow and damage enemies; SOUL consumption and active-cap fuel accounting remain correct. |
| [ ] | 23 | Fragile / Unbreakable Heart | Shared native health | Maximum health increases and normalises correctly on equipment changes; verify each variant's death/break behaviour. |
| [ ] | 24 | Fragile / Unbreakable Greed | Native enemy-death rewards | Mario kills give extra Geo; verify each variant's death/break behaviour. |
| [ ] | 25 | Fragile / Unbreakable Strength | Mario adaptation using native nail damage | Physical damage increases across punches, kicks, dive, stomp and pound. Compare nail upgrades, Fury, Metal and fragile/unbreakable variants. |
| [ ] | 26 | Nailmaster's Glory | Mario cap adaptation | Passive cap upkeep decreases by 25%. Check all caps, reserve SOUL and switching; individual Metal hit costs stay unchanged. |
| [ ] | 27 | Joni's Blessing | Shared native health | Health conversion, damage and healing restrictions work; inspect Hiveblood, Fury and Elegy combinations. |
| [ ] | 28 | Shape of Unn | Native moving-focus handover | Move while focusing, check the form/animation and return position, then try Quick Focus and Deep Focus combinations. |
| [ ] | 29 | Hiveblood | Closer check: native regeneration | A lost mask regenerates after the expected wait; another hit restarts the wait. Check blue-health/Joni rules separately. |
| [ ] | 30 | Dream Wielder | Native Dream Nail handover | Dream Nail charges faster, gives the correct SOUL and returns to Mario cleanly. |
| [ ] | 31 | Dashmaster | Mario adaptation | Ground and aerial long jumps receive 20% more horizontal launch speed once. Ground-pound descent can cancel into long jump; test Sprintmaster combination and dash recharge. |
| [ ] | 32 | Quick Slash | Mario adaptation | Successful punches recover sooner; short-hop kicks return to actionable ground play. Connected aerial kicks recover after the configured delay, 0.2 seconds by default. Misses do not earn hit-confirmed recovery. |
| [ ] | 33 | Spell Twister | Native spell handover | Casting uses less SOUL for each applicable spell; healing costs stay unchanged. |
| [ ] | 34 | Deep Focus | Native focus handover | Healing amount and longer timing work; interruption and Quick Focus/Shape of Unn combinations return control correctly. |
| [ ] | 35 | Grubberfly's Elegy | Mario adaptation using native beams | At eligible health, punches, moving punches, jump kicks and the initial slide-kick strike fire beams. Sustained slides, dives and pounds do not. Check both directions, Mark of Pride, Joni and Fury. |
| [ ] | 36 | Kingsoul / Void Heart | Native passive/world effects | Kingsoul generates SOUL with correct active-cap accounting. Separately check Void Heart's story/world effects and replacement of Kingsoul. |
| [ ] | 37 | Sprintmaster | Mario adaptation using native speed ratios | Ground running speeds up; compare Dashmaster combination, slopes, takeoff, water/walk restrictions and conveyors. |
| [ ] | 38 | Dreamshield | Mario attack reaction plus native shield; closer check | Shield orbits, blocks projectiles and damages normally. Damaging Mario moves send one attack reaction per move, without repeated reactions every frame. |
| [ ] | 39 | Weaversong | Native companions; previously creator-confirmed | Weavers spawn, follow across rooms and damage enemies; inspect Grubsong and Sprintmaster combinations. |
| [ ] | 40 | Grimmchild | Native companion; previously creator-confirmed | Following, targeting, firing, damage and visibility work at the available upgrade levels and after room changes. |
| [ ] | 40 alternative | Carefree Melody | Closer check: native protection | Successful blocks prevent health loss and show feedback; counter/chance handling survives Mario actions and handovers. |

On 5 November, nine focused checks passed against the installed 0.10.119 package:
Quick Slash policy, real-native bonk recovery, attack accounting, combat/control
gates, spell-damage handover, focus/dash handover, moving-focus presentation,
inventory wording, and upgrade/Elegy/Dreamshield gates. These did not launch the
game and do not establish every charm's gameplay, damage or visual behaviour.
No charm gameplay or creator-supplied inventory wording was changed for this list.

Creator feedback, 5 November 2026, local 0.10.120 play session:

> I think most badges seem to work as they used to though?  i tried a good few

This records a positive general playtest result. Individual badges and
combinations were not identified, so no individual review box was changed.
