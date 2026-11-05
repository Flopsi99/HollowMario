using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    internal static class MarioReflection
    {
        internal static void Arm(Harmony harmony,Type controller)
        {
            Type renderer=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            harmony.Patch(AccessTools.Constructor(renderer,new[]{typeof(string)}),
                postfix:new HarmonyMethod(typeof(MarioReflection),nameof(AfterCreated)));
            // The engine may have constructed its playable renderer before us.
            AfterCreated(CapAccess.Read(CapAccess.Read(controller,"Instance"),"Renderer"));
        }
        private static void AfterCreated(object __instance)
        {
            var view=CapAccess.Read(__instance,"Object") as GameObject;
            if(view==null)return;
            int layer=LayerMask.NameToLayer("Player");
            // The egg's native reflection mask is Player-only. This object is
            // visual geometry, with no collider or additional player controller.
            if(layer>=0)view.layer=layer;
        }
    }
}
