using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // A Mage's Roam Range/Firing Range are spatial detectors, not prompts.
    // A second Player collider must not report a false departure from them.
    internal static class MageRangeContacts
    {
        private static Type fsmType,healthType;
        private static int failures,reportedMage;
        internal static void Arm(Harmony harmony)
        {
            fsmType=AccessTools.TypeByName("PlayMakerFSM");healthType=AccessTools.TypeByName("HealthManager");
            foreach(string type in new[]{"Trigger2dEvent","Trigger2dEventLayer"})
                foreach(string phase in new[]{"Enter","Stay","Exit"})
                    harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions."+type),"DoTrigger"+phase+"2D"),
                        prefix:new HarmonyMethod(typeof(MageRangeContacts),nameof(BeforeContact)));
            Type test=AccessTools.TypeByName("HutongGames.PlayMaker.Actions.BoolTest");
            foreach(string method in new[]{"OnEnter","OnUpdate"})
                harmony.Patch(AccessTools.Method(test,method),prefix:new HarmonyMethod(typeof(MageRangeContacts),nameof(BeforeRangeTest)));
        }
        internal static bool IsRange(string owner,string parentFsm)
            =>(owner=="Roam Range"||owner=="Firing Range")&&parentFsm=="Mage";
        private static bool BeforeContact(object __instance,Collider2D __0,System.Reflection.MethodBase __originalMethod)
        {
            // Native Player is layer 9. Leave unrelated physics traffic alone
            // before consulting any FSM/transform hierarchy.
            bool support=Plugin.IsFeetPad(__0);
            if(!support&&(__0==null||__0.gameObject.layer!=9))return true;
            try
            {
                object fsm=CapAccess.Read(__instance,"Fsm");var area=CapAccess.Read(fsm,"GameObject") as GameObject;
                if(area==null||(area.name!="Roam Range"&&area.name!="Firing Range")||area.transform.parent==null)return true;
                bool mage=false;
                foreach(Component parent in area.transform.parent.GetComponents(fsmType))
                    if(IsRange(area.name,Convert.ToString(CapAccess.Read(parent,"FsmName")))){mage=true;break;}
                if(!mage)return true;
                if(support)return false;
                if(__0==null||__originalMethod.Name!="DoTriggerExit2D")return true;
                var follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
                if(controller==null||!CapAccess.Flag(controller,"Wanted"))return true;
                var hero=follow.Hero(controller) as Component;
                if(hero==null||!__0.transform.IsChildOf(hero.transform))return true;
                // A hurtbox/body handover can also produce an exit while the
                // main body still occupies the detector. Do not count the pad.
                var body=hero.GetComponent<BoxCollider2D>();
                if(body==null||!body.enabled||!body.gameObject.activeInHierarchy)return true;
                foreach(Collider2D shape in area.GetComponents<Collider2D>())
                {
                    if(shape==null||!shape.enabled)continue;
                    ColliderDistance2D distance=shape.Distance(body);
                    if(distance.isValid&&distance.isOverlapped)return false;
                }
            }
            catch(Exception error){Warn(error);}
            return true;
        }
        internal static bool LockedFight(string scene,string battleState,bool manual,bool enemyAlive,bool heroAlive,bool mario)
            =>scene=="Ruins1_09"&&battleState=="Started"&&manual&&enemyAlive&&heroAlive&&mario;
        private static bool BeforeRangeTest(object __instance)
        {
            // Only this continuous LOST HERO check is suppressed. The native
            // attack/teleport decisions, manual wake, damage and death remain.
            object flag=CapAccess.Read(__instance,"boolVariable");
            if(Convert.ToString(CapAccess.Read(flag,"Name"))!="Roam Range"||CapAccess.Flag(flag,"Value")
                ||Convert.ToString(CapAccess.Read(CapAccess.Read(__instance,"isFalse"),"Name"))!="LOST HERO"
                ||!CapAccess.Flag(__instance,"everyFrame"))return true;
            try
            {
                object fsm=CapAccess.Read(__instance,"Fsm");
                if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Mage")return true;
                var enemy=CapAccess.Read(fsm,"GameObject") as GameObject;
                if(enemy==null||enemy.scene.name!="Ruins1_09")return true;
                var follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
                if(controller==null||!CapAccess.Flag(controller,"Wanted"))return true;
                var hero=follow.Hero(controller) as Component;if(hero==null)return true;
                bool alive=!CapAccess.Flag(CapAccess.Read(hero,"cState"),"dead");
                var variables=CapAccess.Read(fsm,"Variables");
                bool manual=CapAccess.Flag(CapAccess.Call(variables,"GetFsmBool","Manual Appear"),"Value");
                bool enemyAlive=CapAccess.Number(enemy.GetComponent(healthType),"hp")>0f;
                foreach(Component parent in enemy.GetComponentsInParent(fsmType,true))
                {
                    if(Convert.ToString(CapAccess.Read(parent,"FsmName"))!="Battle Control")continue;
                    string state=Convert.ToString(CapAccess.Read(parent,"ActiveStateName"));
                    if(!LockedFight(enemy.scene.name,state,manual,enemyAlive,alive,true))continue;
                    int id=enemy.GetInstanceID();
                    if(reportedMage!=id)
                    {reportedMage=id;Plugin.Instance?.Log.LogInfo("Kept the Soul Sanctum gate fight active after a lost-range signal.");}
                    return false;
                }
            }
            catch(Exception error){Warn(error);}
            return true;
        }
        private static void Warn(Exception error)
        {if(failures++<3)Plugin.Instance?.Log.LogWarning("Soul Sanctum range contact: "+error.GetBaseException().Message);}
    }
}
