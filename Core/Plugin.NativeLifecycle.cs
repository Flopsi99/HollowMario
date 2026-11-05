using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {
        private static void IndependentHit(object hero)
        {
            object controller=Instance?.knightFollow?.Controller();
            if(controller==null||!ReferenceEquals(hero,Instance.knightFollow.Hero(controller)))return;
            bool live=Instance.knightFollow.MarioAlive(controller);
            if(DamageReaction.PresentationHit&&DamageReaction.ContactHazard==1)
            {
                if(!ConversationMario.InterruptForDamage(hero)&&!live)return;
            }
            else if(!live)return;
            UnityEngine.Transform source=(DamageReaction.ContactSource as UnityEngine.GameObject)?.transform
                ??(DamageReaction.ContactSource as UnityEngine.Component)?.transform;
            if(source!=null)AccessTools.Method(controller.GetType(),"SetDamageSource").Invoke(controller,new object[]{source.position});
            bool previous=NativeDamageEligibility.BeginConfirmedHit();
            try{AccessTools.Method(controller.GetType(),"OnKnightHit").Invoke(controller,new object[]{DamageReaction.ContactDamage,DamageReaction.ContactHazard});}
            finally{NativeDamageEligibility.EndConfirmedHit(previous);}
        }
        private static bool IndependentContactGuard(object __instance,object[] __args)
        {
            object controller=Instance?.knightFollow?.Controller();
            if(controller==null||!Instance.knightFollow.MarioAlive(controller)||!ReferenceEquals(__instance,Instance.knightFollow.Hero(controller)))return true;
            int hazard=__args!=null&&__args.Length>3?Convert.ToInt32(__args[3]):1;
            bool blocked=(bool)AccessTools.Method(controller.GetType(),"BlocksContactDamage").Invoke(controller,new object[]{hazard,HasHitProtection(__instance)});
            if(blocked)Instance.Log.LogInfo("Independent native invulnerability rejected contact; hazard="+hazard+", action="+Instance.knightFollow.Action(controller).ToString("X"));
            return !blocked;
        }
        // Mario's sound thread reads the game engine's Mario memory. When the game takes the Knight
        // back it frees that memory, and a sound tick landing in that window walks into freed memory
        // and crashes (usually during a room change). So no sound tick runs while Mario isn't there.
        private static bool AudioTickPrefix()
        {
            return AudioTicksAllowed || OpeningMario.Presenting || BenchMario.Presenting || ConversationMario.Presenting;
        }

        private static bool HasLiveMario()
        {
            return Instance != null && Instance.knightFollow != null
                && Instance.knightFollow.MarioAlive(Instance.knightFollow.Controller());
        }

        // Mario is never thrown away at a room change: his sound keeps a pointer into his own memory,
        // and once that memory is gone the sound engine can read it and take the whole game down.
        private void KeepMarioLoaded(Type nativeType)
        {
            if (nativeType == null)
            {
                Log.LogWarning("Mario's own engine wasn't found; he is still unloaded at every room change.");
                return;
            }
            MethodInfo create = AccessTools.Method(nativeType, "sm64_mario_create", new[] { typeof(float), typeof(float), typeof(float) });
            MethodInfo delete = AccessTools.Method(nativeType, "sm64_mario_delete", new[] { typeof(int) });
            nativeMarioPosition = AccessTools.Method(nativeType, "sm64_set_mario_position", new[] { typeof(int), typeof(float), typeof(float), typeof(float) });
            nativeMarioVelocity = AccessTools.Method(nativeType, "sm64_set_mario_velocity", new[] { typeof(int), typeof(float), typeof(float), typeof(float) });
            nativeMarioAction = AccessTools.Method(nativeType, "sm64_set_mario_action", new[] { typeof(int), typeof(uint) });
            nativeMarioHealth = AccessTools.Method(nativeType, "sm64_set_mario_health", new[] { typeof(int), typeof(ushort) });
            if (create == null || delete == null || nativeMarioPosition == null || nativeMarioVelocity == null || nativeMarioAction == null || nativeMarioHealth == null)
            {
                Log.LogWarning("Mario's unload step wasn't found; he is still unloaded at every room change.");
                return;
            }
            // The host owns pooling, but a forced handover still has to cancel
            // the presentation actor's queued/playing sleep voice.
            harmony.Patch(delete, prefix: new HarmonyMethod(typeof(Plugin), nameof(IndependentParkPrefix)));
            try{BenchRestAudio.Install(Path.Combine(Path.GetDirectoryName(nativeType.Assembly.Location),"sm64.dll"));}
            catch(Exception error){Log.LogWarning("Mario's sleep-sound cleanup could not be armed: "+error.GetBaseException().Message);}
            try{NativeDamageEligibility.Install(Path.Combine(Path.GetDirectoryName(nativeType.Assembly.Location),"sm64.dll"));}
            catch(Exception error){Log.LogWarning("Mario's current-frame damage eligibility could not be armed: "+error.GetBaseException().Message);}
            Log.LogInfo("Mario now stays loaded between rooms; the game sets him aside instead of freeing him.");
        }
        private static void IndependentParkPrefix()=>BenchRestAudio.StopSleeping();

    }
}
