using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Keep native charm effects attached to the shared hero. Thorns borrows its
    // own four hitboxes/damage FSMs, without the Knight animation/control FSM.
    internal static class NativeCharmEffects
    {
        private static object hero,counter;
        private static GameObject thorns;
        private static float delay,duration,left;
        private static bool pending,ownsBurst;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony,Type controller,ManualLogSource logger)
        {
            log=logger;
            try
            {
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("PlayMakerFSM"),"SendEvent",new[]{typeof(string)}),
                    prefix:new HarmonyMethod(typeof(NativeCharmEffects),nameof(BeforeEvent)));
                DamageReaction.Confirmed+=Damaged;
            }
            catch(Exception error){Warn(error);}
        }
        internal static void Activated(object controller)
        {
            Forget();
            try
            {
                hero=Plugin.Instance?.knightFollow?.Hero(controller);
                if(!(hero is Component body))return;
                counter=CapAccess.Read(hero,"fsm_thornCounter");
                thorns=body.transform.Find("Charm Effects/Thorn Hit")?.gameObject;
                if(counter!=null&&thorns!=null)
                {
                    delay=WaitIn("Counter Start");duration=WaitIn("Counter");
                }
            }
            catch(Exception error){thorns=null;Warn(error);}
        }
        private static float WaitIn(string name)
        {
            foreach(object state in (Array)CapAccess.Read(counter,"FsmStates"))
                if(Convert.ToString(CapAccess.Read(state,"Name"))==name)
                    foreach(object action in (Array)CapAccess.Read(state,"Actions"))
                        if(action.GetType().Name=="Wait")
                        {
                            float value=CapAccess.Number(CapAccess.Read(action,"time"),"Value");
                            if(value>0f&&!float.IsInfinity(value)&&!float.IsNaN(value))return value;
                        }
            throw new MissingMemberException("Native Thorns timing: "+name);
        }
        private static bool MarioOwnsHero()
        {
            KnightResetFollow follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
            if(hero==null||controller==null||!follow.MarioAlive(controller)||!ReferenceEquals(hero,follow.Hero(controller)))return false;
            object flags=CapAccess.Read(hero,"cState"),data=CapAccess.Read(hero,"playerData");
            return !CapAccess.Flag(flags,"dead")&&!CapAccess.Flag(flags,"hazardDeath")&&!CapAccess.Flag(flags,"transitioning")
                &&!follow.ControlHeld(controller)&&CapAccess.Number(data,"health")+CapAccess.Number(data,"healthBlue")>0;
        }
        private static void Damaged(object hurtHero)
        {
            try
            {
                if(!ReferenceEquals(hurtHero,hero)||thorns==null||ownsBurst||!MarioOwnsHero())return;
                object data=CapAccess.Read(hero,"playerData");if(!CapAccess.Flag(data,"equippedCharm_12"))return;
                // The native Knight starts Thorns after recoil; retain that wait and
                // its actual windup. No input, gravity, animation or immunity writes.
                left=delay+CapAccess.Number(hero,CapAccess.Flag(data,"equippedCharm_4")?"RECOIL_DURATION_STAL":"RECOIL_DURATION");
                pending=true;
            }
            catch(Exception error){Warn(error);}
        }
        private static bool BeforeEvent(object __instance,string __0)
        {
            if(__0!="THORN COUNTER"||!ReferenceEquals(__instance,counter)||!MarioOwnsHero())return true;
            // Also consume any native queued event before it can relinquish control.
            if(!pending&&!ownsBurst)Damaged(hero);
            return false;
        }
        internal static void Tick(float dt)
        {
            if((!pending&&!ownsBurst)||dt<=0f)return;
            try
            {
                if(!MarioOwnsHero()||!CapAccess.Flag(CapAccess.Read(hero,"playerData"),"equippedCharm_12")){CancelBurst();return;}
                left-=dt;if(left>0f)return;
                if(ownsBurst){CancelBurst();return;}
                pending=false;
                // Preserve another owner's active native attack. Only deactivate a
                // burst we started; its own receivers handle shape, damage and repeats.
                if(thorns.activeSelf)return;
                ownsBurst=true;left=duration;thorns.SetActive(true);
            }
            catch(Exception error){CancelBurst();Warn(error);}
        }
        private static void CancelBurst()
        {
            pending=false;left=0;
            if(ownsBurst&&thorns!=null)thorns.SetActive(false);
            ownsBurst=false;
        }
        internal static void Forget(){CancelBurst();AttackCharmEffects.Forget();hero=counter=null;thorns=null;}
        internal static void Dispose(){Forget();DamageReaction.Confirmed-=Damaged;}
        private static void Warn(Exception error){if(failures++<3)log?.LogWarning("Mario's native charm effects: "+error.GetBaseException().Message);}
    }
}
