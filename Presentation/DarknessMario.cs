using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The engine's hide-Knight loop exempts renderers named light/vignette,
    // but the vignette's Darkness Border/Plates children have other names.
    // Exempt the entire native effect before that loop records or hides it.
    internal static class DarknessMario
    {
        private static FieldInfo heroField,vignetteField;
        [ThreadStatic] private static Transform currentVignette;

        internal static void Arm(Harmony harmony,Type controller)
        {
            heroField=AccessTools.Field(controller,"hero");
            vignetteField=AccessTools.Field(AccessTools.TypeByName("HeroController"),"vignette");
            if(heroField==null||vignetteField==null)throw new MissingMemberException("Native darkness hierarchy");
        }
        private static void BeforeHide(object __instance,out Transform __state)
        {
            __state=currentVignette;currentVignette=null;
            var hero=heroField.GetValue(__instance) as Component;
            if(hero==null)return;
            var vignette=vignetteField.GetValue(hero) as Component;
            currentVignette=vignette!=null?vignette.transform:hero.transform.Find("Vignette");
        }
        private static void AfterHide(Transform __state)=>currentVignette=__state;

        private static string CullName(UnityEngine.Object value)
        {
            var renderer=value as Renderer;
            if(renderer!=null&&currentVignette!=null&&renderer.transform.IsChildOf(currentVignette))
                return "vignette"; // Only the local hide decision; never rename an object.
            return value.name;
        }
        internal static IEnumerable<CodeInstruction> KeepDarkness(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo name=AccessTools.PropertyGetter(typeof(UnityEngine.Object),"name");
            MethodInfo replacement=AccessTools.Method(typeof(DarknessMario),nameof(CullName));
            int count=0;
            foreach(CodeInstruction instruction in instructions)
            {
                if(instruction.Calls(name))
                {instruction.opcode=OpCodes.Call;instruction.operand=replacement;count++;}
                yield return instruction;
            }
            if(count!=1)throw new InvalidOperationException("Native renderer-hide name check changed: "+count);
        }
    }
}
