using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    internal static class CrystalPeakCrushers
    {
        private static float SpeedFor(string controller)
        {
            switch(controller)
            {
                // Keep the whole double-stomp pair at one rate in each room.
                case "mines_stomper_fast1":
                case "mines_stomper_fast2":
                // This chest-room group has a shorter cycle and narrow openings.
                case "mines_stomper_offset_big_scene 02":return .35f;
                default:return .5f;
            }
        }

        internal static void Arm(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("StopAnimatorsAtPoint"),"Awake"),
                postfix:new HarmonyMethod(typeof(CrystalPeakCrushers),nameof(AfterAwake)));
        }

        private static void AfterAwake(Component __instance,Animator ___animator)
        {
            if(__instance==null||___animator==null)return;
            string name=__instance.name;
            if(name!="mines_stomper_02"&&name!="mines_stomper_02(Clone)")return;
            var controller=___animator.runtimeAnimatorController;
            if(controller==null||!controller.name.StartsWith("mines_stomper_",StringComparison.Ordinal))return;
            // Movement, dwell periods and safe-stop events share this clock.
            // Leave the native stop controller and the lever's own animator alone.
            // Each matching pattern keeps one shared rate and its authored offsets.
            ___animator.speed=SpeedFor(controller.name);
        }
    }
}
