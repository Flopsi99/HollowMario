using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // One generic native hit bridge for every direct Mario move. Native blocking,
    // damage, death and nail events remain authoritative; pay only for real HP loss.
    internal static class MetalCap
    {
        [ThreadStatic] private static object directController;
        [ThreadStatic] private static bool recovering;
        [ThreadStatic] private static DamageFrame damageFrame;
        private sealed class DamageFrame {internal object Data;internal bool Accepted,Bounced,Hazard;internal int Kind;}
        private static float lastLavaContact=float.NegativeInfinity;
        private static readonly HashSet<object> charged=new HashSet<object>();
        private static FieldInfo hp,hitSource,hitType,hitDamage;
        private static MethodInfo takeDamage;
        private static MethodInfo resetAirMoves;
        private static FieldInfo dashCooldown;
        private static Type sideType;
        private static object attackController;
        private static uint attack;
        private static int animation,frame;
        private static bool haveAttack;
        private static ManualLogSource log;
        private static int failures,impactNotes,graceNotes;
        internal static bool Ready {get;private set;}
        internal static bool Recovering=>recovering;
        internal struct HitFrame {internal object Target;internal int Before;}

        internal static void Arm(Harmony harmony,Type controller,ManualLogSource logger)
        {
            log=logger;Ready=false;
            try
            {
                Type health=AccessTools.TypeByName("HealthManager"),hero=AccessTools.TypeByName("HeroController");
                MethodInfo hit=AccessTools.Method(health,"Hit"),tick=AccessTools.Method(controller,"Tick");
                Type payload=hit?.GetParameters()[0].ParameterType;
                hp=AccessTools.Field(health,"hp");hitSource=AccessTools.Field(payload,"Source");
                hitType=AccessTools.Field(payload,"AttackType");hitDamage=AccessTools.Field(payload,"DamageDealt");
                takeDamage=AccessTools.Method(hero,"TakeDamage");sideType=takeDamage?.GetParameters()[1].ParameterType;
                resetAirMoves=AccessTools.Method(hero,"ResetAirMoves");dashCooldown=AccessTools.Field(hero,"dashCooldownTimer");
                MethodInfo takeHealth=AccessTools.Method(AccessTools.TypeByName("PlayerData"),"TakeHealth",new[]{typeof(int)});
                MethodInfo reaction=AccessTools.Method(controller,"OnKnightHit");
                if(hit==null||tick==null||hp==null||hitSource==null||hitType==null||hitDamage==null||sideType==null||takeHealth==null||reaction==null||!CapNative.LavaReady)
                    throw new MissingMemberException("Metal Cap's native hit/recovery bridge");
                harmony.Patch(tick,prefix:new HarmonyMethod(typeof(MetalCap),nameof(BeforeTick)){priority=Priority.First+400},
                    finalizer:new HarmonyMethod(typeof(MetalCap),nameof(FinishTick)));
                harmony.Patch(hit,prefix:new HarmonyMethod(typeof(MetalCap),nameof(BeforeHit)),
                    finalizer:new HarmonyMethod(typeof(MetalCap),nameof(FinishHit)));
                // Snapshot the damage event (+100) before a lava bounce skips the body;
                // its nested-call finalizer must always have a matching saved frame.
                harmony.Patch(takeDamage,prefix:new HarmonyMethod(typeof(MetalCap),nameof(BeforeDamage)){priority=Priority.First+50},
                    finalizer:new HarmonyMethod(typeof(MetalCap),nameof(FinishDamage)));
                harmony.Patch(takeHealth,prefix:new HarmonyMethod(typeof(MetalCap),nameof(BeforeHealth)));
                harmony.Patch(reaction,prefix:new HarmonyMethod(typeof(MetalCap),nameof(BeforeReaction)){priority=Priority.First});
                Ready=true;
            }
            catch(Exception error){Warn(error);}
        }

        private static bool BeforeDamage(object __instance,int __2,int __3,out object __state)
        {
            __state=damageFrame;damageFrame=null;
            if(!CapSystem.MetalFor(__instance))return true;
            damageFrame=new DamageFrame{Data=CapAccess.Read(__instance,"playerData"),Kind=__3,
                Hazard=!recovering&&__2>0&&(__3==2||__3==4||__3==5)};
            // All native reset hazards except acid. Forced void recovery must still reset.
            if(!damageFrame.Hazard)return true;
            try
            {
                float now=Time.time;
                // A continuing overlap is the same impact even while input briefly
                // drops out. Check/refresh before the gate that can choose native hurt.
                if(now-lastLavaContact<=.25f)
                {
                    lastLavaContact=now;damageFrame.Bounced=true;
                    if(graceNotes++<2)log?.LogInfo("Metal hazard overlap: grace refreshed; no relaunch or extra SOUL charge.");
                    return false;
                }
                KnightResetFollow follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
                if(controller==null||follow.ControlHeld(controller)||!follow.AcceptingInput(controller))return true;
                damageFrame.Bounced=true;
                CapNative.LavaBounce(follow.MarioId(controller));
                lastLavaContact=now;
                if(CapSystem.RefreshMetalAirMoves)
                {
                    resetAirMoves?.Invoke(__instance,null);
                    dashCooldown?.SetValue(__instance,0f);
                    ShadeLongJump.Refill(controller,follow);
                }
                NoteImpact(__3,"lava bounce");
                CapSystem.PayMetalImpact();
                return false;
            }
            catch(Exception error){damageFrame.Bounced=false;Warn(error);return true;}
        }
        // The base mod's unconditional hit postfix must not overwrite our lava action.
        private static bool BeforeReaction()=>damageFrame?.Bounced!=true;
        private static bool BeforeHealth(object __instance,int __0)
        {
            if(damageFrame==null||!ReferenceEquals(damageFrame.Data,__instance)||__0<=0)return true;
            damageFrame.Accepted=true;DamageReaction.ConfirmMasklessHit(__instance);
            // An accepted native fallback must seed the same grace as a lava launch.
            // Rejected hits never reach this point and cannot grant free overlap grace.
            if(damageFrame.Hazard)lastLavaContact=Time.time;
            return false;
        }
        private static void FinishDamage(object __state)
        {
            bool accepted=damageFrame?.Accepted==true;
            bool hazard=damageFrame?.Hazard==true;int kind=damageFrame?.Kind??0;
            damageFrame=__state as DamageFrame;
            if(accepted)
            {
                if(hazard)NoteImpact(kind,"native hurt fallback");
                CapSystem.PayMetalImpact();
            }
        }

        private static void BeforeTick(object __instance,out object __state)
        {
            __state=directController;directController=null;
            KnightResetFollow follow=Plugin.Instance?.knightFollow;
            if(CapSystem.Metallic&&ReferenceEquals(follow?.Controller(),__instance))
            {directController=__instance;Observe(__instance,follow);}
        }
        private static void FinishTick(object __state){directController=__state;}

        private static void Observe(object controller,KnightResetFollow follow)
        {
            uint action=follow.Action(controller),family=Family(action);
            int anim=follow.Animation(controller),nextFrame=follow.AnimFrame(controller);
            bool grouped=family!=action||family==0x008008A9u||family==0x0188088Au;
            if(!haveAttack||!ReferenceEquals(attackController,controller)||family!=attack
                ||(!grouped&&(anim!=animation||nextFrame<frame)))charged.Clear();
            attackController=controller;attack=family;animation=anim;frame=nextFrame;haveAttack=true;
        }
        private static uint Family(uint action)
            => action==0x0080023Cu?0x008008A9u:action==0x00880456u?0x0188088Au:action;

        private static void BeforeHit(object __instance,ref object __0,out HitFrame __state)
        {
            __state=default;
            if(directController==null||!CapSystem.Metallic||__0==null)return;
            try
            {
                KnightResetFollow follow=Plugin.Instance?.knightFollow;
                object hero=follow?.Hero(directController);
                if(!(hero is Component body)||!CapSystem.MetalFor(hero)
                    ||!ReferenceEquals(hitSource.GetValue(__0),body.gameObject)||Convert.ToInt32(hitType.GetValue(__0))!=0)return;
                int damage=Convert.ToInt32(hitDamage.GetValue(__0));if(damage<=0)return;
                Observe(directController,follow);
                __state=new HitFrame{Target=__instance,Before=Convert.ToInt32(hp.GetValue(__instance))};
                // HitInstance is a value type. Change only this call's copy; preserve
                // source, nail classification, angle, knockback and armour rules.
                hitDamage.SetValue(__0,(int)Math.Min(int.MaxValue,Math.Max(1,Math.Round(damage*CapDefinition.Metal.DamageMultiplier,MidpointRounding.AwayFromZero))));
            }
            catch(Exception error){__state=default;Warn(error);}
        }
        private static void FinishHit(HitFrame __state)
        {
            if(__state.Target==null||!CapSystem.Metallic)return;
            try
            {
                if(Convert.ToInt32(hp.GetValue(__state.Target))<__state.Before&&charged.Add(__state.Target))CapSystem.PayMetalHit();
            }
            catch(Exception error){Warn(error);}
        }

        internal static bool ReturnFromVoid(object controller,KnightResetFollow follow)
        {
            object hero=follow?.Hero(controller);
            if(!Ready||hero==null||!follow.MarioAlive(controller)||follow.Busy(controller)||follow.ControlHeld(controller)||!follow.AcceptingInput(controller))return false;
            bool outer=recovering;
            try
            {
                recovering=true;
                object side=sideType.IsEnum?Enum.ToObject(sideType,0):Convert.ChangeType(0,sideType);
                takeDamage.Invoke(hero,new object[]{(hero as Component)?.gameObject,side,1,2});
                return true;
            }
            catch(Exception error){Warn(error);return false;}
            finally{recovering=outer;CapSystem.Stop();}
        }

        internal static void ResetDirectAttack(){charged.Clear();haveAttack=false;attackController=null;}
        internal static void ForgetAttack(){ResetDirectAttack();lastLavaContact=float.NegativeInfinity;}
        private static void NoteImpact(int kind,string route)
        {if(impactNotes++<8)log?.LogInfo("Metal hazard impact charged: kind="+kind+", route="+route+", time="+Time.time.ToString("0.000")+".");}
        private static void Warn(Exception error){if(failures++<6)log?.LogWarning("Metal Cap: "+error.GetBaseException().Message);}
    }
}
