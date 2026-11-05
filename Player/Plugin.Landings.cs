using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {
        private void ArmHardLandings(Type hero)
        {
            hardLandingReset = AccessTools.Method(hero, "ResetHardLandingTimer");
            var landing = AccessTools.Method(hero, "DoHardLanding");
            if (hardLandingReset != null && landing != null)
                harmony.Patch(landing, postfix: new HarmonyMethod(typeof(Plugin), nameof(HardLandingFinished)));
            else Log.LogWarning("The game's forced landing cleanup could not be armed.");
        }

        private static void HardLandingFinished(object __instance)
        {
            var follow = Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || !follow.MarioAlive(controller)
                || !ReferenceEquals(__instance, follow.Hero(controller))
                || !(__instance is Behaviour hero) || hero.enabled) return;
            try
            {
                // Native collision callbacks still run while HeroController.Update is disabled.
                // Keep the real impact once, then consume its willHardLand/fall timers before
                // KeepInteractable restores Mario's state and the next contact can retrigger it.
                hardLandingReset.Invoke(__instance, null);
                if (!follow.Busy(controller) && !follow.ControlHeld(controller) && follow.AcceptingInput(controller))
                    follow.KeepInteractable(controller);
            }
            catch (Exception error)
            {
                if (hardLandingFailures++ < 4)
                    Instance?.Log.LogWarning("Mario's forced landing could not finish: " + error.GetBaseException().Message);
            }
        }
    }
}
