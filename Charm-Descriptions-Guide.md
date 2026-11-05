# Editing the charm descriptions

Open **Charm-Descriptions.yaml** beside this guide. It contains the sixteen charm
slots we explicitly adapted, confirmed in play, or source-verified through the
same SOUL path. Strength has separate normal, unbreakable and broken variants.

The order follows what players do: movement, attack reach, damage, reactive/shadow
effects, SOUL/caps, then exploration/collection.

Edit **new_description** under each charm. Your wording is authoritative: preserve
its vanilla tone, light Mario nods, and lack of exact numerical effects. Do not
rewrite spelling, grammar, terminology or punctuation without your request.
Keep the indentation below `|-` and use
blank lines for paragraphs. Ordinary punctuation is fine. To leave a description
unchanged, replace its field with `new_description: ""`.

Original game descriptions are not included in this source. **mario_behavior**
reminds you what the mod actually does, including combinations and limits.
**verification** distinguishes completed code work from what you have personally
tested. These reference notes are not intended to appear in the game.

The build now reads the filled texts from this file verbatim. Only those texts
are compiled into the companion; original descriptions and reference notes are
excluded. Editing this file does not change a running or already installed build:
tell me when you want a fresh build and installation after another text edit.
The replacement applies to the English charm inventory while Mario is selected.
Native Knight wording, other languages, names and shop text keep their own paths.

## What we already changed

| Charm | Mario adaptation |
|---|---|
| Sprintmaster | Applies the native running boost to Mario's ground movement, including the stronger Dashmaster combination. |
| Dashmaster | Adds 20% horizontal long-jump launch speed and permits the ground-pound cancel during descent, subject to the shared air-dash/cooldown rules. |
| Longnail / Mark of Pride | Increase physical attack reach by 15% / 25%; 40% together. Mario's incoming hurtbox does not grow. |
| Fragile / Unbreakable Strength | Feed native Strength-adjusted nail damage into Mario's physical moves, preserving native broken-charm, rounding and binding rules. |
| Fury of the Fallen | Apply the native active Fury multiplier to physical moves, once, with native health rules. |
| Heavy Blow | Apply native increased enemy knockback to Mario's physical hits; Mario's bounce strength stays the same. |
| Sharp Shadow | Make only the live shadow phase of a long jump attack through the existing dive path and its 2x damage. |
| Thorns of Agony | Trigger native thorn hitboxes after an accepted Mario damage reaction, retaining native timing without switching into the Knight's thorn animation. |
| Defender's Crest | Preserve the continuous odour particles that the base Mario mod hid. Native cloud frequency and damage stay in charge. |
| Nailmaster's Glory | Reduce passive cap SOUL upkeep by 25%; default Vanish/Metal 8 to 6 per second, Wing 6 to 4.5. |
| Soul Catcher / Soul Eater | Retain native extra attack SOUL. With Metal, reduce successful-hit fuel cost from 5 to 3 / 1; both together still cost 1. Active caps cannot be refuelled by SOUL generation. |

Gathering Swarm, Wayward Compass, Grubsong and ordinary Soul Catcher already work
through the native game, as you confirmed. Soul Eater shares Catcher's verified
native routine, but its direct gameplay check is still outstanding.

The checklist's newer damage/Sharp Shadow/Thorns/Crest adaptations have isolated
checks recorded, with actual gameplay still pending. Vanish cap behavior was
accepted; the remaining cap combinations have their own limits noted in the YAML.

The bottom of the YAML preserves the rest of the forty-slot checklist as a
compact backlog. Those entries do not have replacement drafts: some effects are
expected to carry over, some need an audit, and Steady Body, Quick Slash and
Grubberfly's Elegy still lack an agreed Mario equivalent. Their names are not a
claim that their original descriptions accurately describe Mario's behavior.

The extracted original game wording remains a private editing reference; the
full editing file is not a game-data file to distribute with the mod. Gameplay
and charm balance are unaffected by this description integration.
