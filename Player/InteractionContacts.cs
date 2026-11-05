using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Mario's support sensor and the solid Knight collider are one player.
    // Native NPC tracking adds a GameObject once but removes it on ANY collider
    // exit. A handover removes the sensor while the body is still in range.
    internal static class InteractionContacts
    {
        private static Type fsmType;
        private static int failures;
        internal static void Arm(Harmony harmony)
        {
            fsmType=AccessTools.TypeByName("PlayMakerFSM");
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("TrackTriggerObjects"),"OnTriggerExit2D"),
                prefix:new HarmonyMethod(typeof(InteractionContacts),nameof(BeforeTrackedExit)){priority=Priority.First+300});
            // The state machine's OUT OF RANGE event must agree with its counter.
            foreach(string type in new[]{"Trigger2dEvent","Trigger2dEventLayer"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions."+type),"DoTriggerExit2D"),
                    prefix:new HarmonyMethod(typeof(InteractionContacts),nameof(BeforeEventExit)){priority=Priority.First+300});
        }
        private static bool BeforeTrackedExit(object __instance,Collider2D __0)
        {
            var tracker=__instance as Component;
            return tracker==null||!StillInside(tracker.gameObject,__0);
        }
        private static bool BeforeEventExit(object __instance,Collider2D __0)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(!ConversationMario.KnownInteraction(Convert.ToString(CapAccess.Read(fsm,"Name"))))return true;
            return !StillInside(CapAccess.Read(fsm,"GameObject") as GameObject,__0,true);
        }
        private static bool StillInside(GameObject range,Collider2D leaving,bool known=false)
        {
            try
            {
                if(range==null||leaving==null||!range.activeInHierarchy)return false;
                var follow=Plugin.Instance?.knightFollow;
                object controller=follow?.Controller();
                var hero=controller==null?null:follow.Hero(controller) as Component;
                if(hero==null||(leaving.gameObject!=hero.gameObject&&!Plugin.IsFeetPad(leaving)))return false;
                if(!known)
                {
                    foreach(Component fsm in range.GetComponents(fsmType))
                        if(ConversationMario.KnownInteraction(Convert.ToString(CapAccess.Read(fsm,"FsmName"))))
                        {known=true;break;}
                    if(!known)return false;
                }
                // Query geometry only on an exit, never each frame. Do not count
                // the collider being removed, enemies or private poses. Only our
                // dedicated child probe joins the root body's interaction reach.
                Collider2D[] areas=range.GetComponents<Collider2D>();
                foreach(Collider2D player in Shapes(hero))
                {
                    if(player==null||player==leaving||(Plugin.IsFeetPad(player)&&Plugin.IsFeetPad(leaving))
                        ||!player.enabled||!player.gameObject.activeInHierarchy)continue;
                    foreach(Collider2D area in areas)
                    {
                        if(area==null||!area.enabled||!area.gameObject.activeInHierarchy)continue;
                        ColliderDistance2D distance=area.Distance(player);
                        if(distance.isValid&&distance.isOverlapped)return true;
                    }
                }
            }
            catch(Exception error){if(failures++<3)Plugin.Instance?.Log.LogWarning("NPC contact handover: "+error.GetBaseException().Message);}
            return false;
        }
        private static System.Collections.Generic.IEnumerable<Collider2D> Shapes(Component hero)
        {
            foreach(Collider2D body in hero.GetComponents<Collider2D>())yield return body;
            Collider2D probe=Plugin.InteractionSensor;
            if(probe!=null)yield return probe;
        }
    }
}
