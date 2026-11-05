using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // DJ Control briefly swaps the Hero for its own wing-spreading double.
    // Cover that double, then follow the real Hero during the native fall.
    // Its actions still own the reward, movement, effects, waits and popup.
    internal static class MonarchWingsMario
    {
        internal const string Group="monarch-pickup";
        internal const int TripleLand=0xC0,ArmsOutFrame=8;
        private static Type heroType;
        private static GameObject cutscene;
        private static Transform actor;
        private static readonly List<MeshRenderer> hidden=new List<MeshRenderer>();
        private static readonly List<bool> wasHidden=new List<bool>();
        private static int phase;
        private static float started,feetOffset;
        internal static bool OwnsSequence=>phase!=0;
        internal static bool Active=>OwnsSequence&&actor!=null&&actor.gameObject.activeInHierarchy;

        internal static int Phase(string fsm,string owner,string state)
        {
            if(fsm!="DJ Control"||(owner!="Shiny Item DJ"&&owner!="Shiny Item DJ(Clone)"))return -1;
            switch(state)
            {
                case "Get1":return 1;
                case "Get2":return 2;
                case "Get End":return 3;
                case "Fall":return 4;
                case "Land":case "Return Control":case "Idle":return 0;
                default:return -1;
            }
        }
        internal static void Arm(Harmony harmony,Type hero)
        {
            heroType=hero;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(MonarchWingsMario),nameof(BeforeState)));
            harmony.Patch(AccessTools.Method(hero,"RegainControl"),
                postfix:new HarmonyMethod(typeof(MonarchWingsMario),nameof(AfterControl)));
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="DJ Control")return;
            var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(owner==null)return;
            int next=Phase("DJ Control",owner.name,Convert.ToString(CapAccess.Read(__instance,"Name")));
            if(next<0)return;
            if(next==0){ConversationMario.ReleaseActionHold(Group);Forget();return;}
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;
                var proxy=CapAccess.Read(CapAccess.Call(CapAccess.Read(fsm,"Variables"),"GetFsmGameObject","Cutscene"),"Value") as GameObject;
                if(hero==null||(next<4&&proxy==null))return;
                if(!ConversationMario.StartAction(hero,next==4?ConversationMario.Fall:TripleLand,0f,false,
                    restart:next==1,holdControl:true,group:Group))return;
                if(cutscene!=proxy)
                {
                    Forget();cutscene=proxy;
                    if(proxy!=null)foreach(var body in proxy.GetComponentsInChildren<MeshRenderer>(true))
                    {hidden.Add(body);wasHidden.Add(body.forceRenderingOff);}
                }
                var box=hero.GetComponent<BoxCollider2D>();
                feetOffset=box!=null?box.bounds.min.y-hero.transform.position.y:0f;
                actor=next==4?hero.transform:proxy.transform;phase=next;started=Time.time;
                HideDouble();
            }
            catch(Exception error)
            {Forget();ConversationMario.ReleaseActionHold(Group);Plugin.Instance?.Log.LogWarning("Mario's Monarch Wings pickup: "+error.GetBaseException().Message);}
        }
        internal static ArrivalMario.Sample At(int stage,float elapsed)
            =>stage==4?new ArrivalMario.Sample{Animation=ConversationMario.Fall,Frame=-1}
                :new ArrivalMario.Sample{Animation=TripleLand,Frame=stage==1
                    ?Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f,elapsed)*30f),0,ArmsOutFrame):ArmsOutFrame};
        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(!Active)return false;
            HideDouble();sample=At(phase,Time.time-started);return true;
        }
        private static void HideDouble(){foreach(var body in hidden)if(body!=null)body.forceRenderingOff=true;}
        internal static Vector3 Anchor=>actor.position+Vector3.up*feetOffset;
        private static void AfterControl(object __instance)
        {
            if(OwnsSequence&&ReferenceEquals(__instance,CapAccess.Read(heroType,"instance")))
            {ConversationMario.ReleaseActionHold(Group);Forget();}
        }
        internal static void Forget()
        {
            for(int i=0;i<hidden.Count;i++)if(hidden[i]!=null)hidden[i].forceRenderingOff=wasHidden[i];
            hidden.Clear();wasHidden.Clear();cutscene=null;actor=null;phase=0;started=feetOffset=0f;
        }
    }
}
