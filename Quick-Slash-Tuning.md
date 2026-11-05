# Quick Slash air recovery

For ordinary aerial kicks, the default is **0.2 seconds after your kick first damages an enemy**.
Mario then returns to an actionable fall, allowing another kick, dive or ground
pound. It no longer waits for the full kick animation. Hitting more enemies with
that same kick does not restart the timer.

A connected short-hop kick instead returns Mario immediately to nearby ground
in neutral idle. This is recognized when the kick starts on the ground or near
the floor during the first three jump steps. The 0.2-second setting applies to
ordinary aerial recovery, not that immediate low-kick return.

The editable setting is **QuickSlashAirRecoverySeconds**, in the **Charms**
section of the [companion mod's settings](<E:/SteamLibrary/steamapps/common/Hollow Knight/BepInEx/config/gg.melty.hollowmario.cfg>).

Change the number after `=`. For example:

- `0.2` is the requested default.
- `0.1` recovers faster.
- `0.3` recovers more slowly.

Edit with the game closed, save, then play normally. No rebuild is needed.
Use a decimal point. Timing follows Mario's simulation, in roughly 0.033-second
steps, and pauses when his simulation pauses. Zero deliberately removes the wait;
negative or invalid numbers fall back to 0.2.

The setting is present at 0.2 seconds.
The update is ready for your next normal launch. Live gameplay feel remains for
you to check.
The same update includes the correction for your edited charm descriptions.
