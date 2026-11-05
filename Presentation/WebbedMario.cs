using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The Spider's Den owns its listeners, struggle counter, web-cut events and
    // release physics. Hide only the separate Knight mesh, preserving its children.
    internal static class WebbedMario
    {
        internal const int Shock=0x7A,ShockFrames=6;
        internal const float ShockFramesPerSecond=4f;
        private static Type heroType;
        private static Transform actor;
        private static MeshRenderer knight;
        private static SpriteRenderer web;
        private static Vector3 localFeet;
        private static bool oldHidden;
        private static int oldLayer,oldOrder;
        private static float age;
        private static int phase;
        private static int traces;
        internal static bool OwnsSequence=>phase!=0;
        internal static bool AllowsPresentation(string group,bool hazard)
            =>!OwnsSequence||group=="web"||group=="death"||hazard;
        internal static void Trace(string message)
        {if(traces++<60)Plugin.Instance?.Log.LogInfo("Web escape: phase="+phase+"; "+message);}
        internal static bool Active=>actor!=null&&actor.gameObject.activeInHierarchy;
        internal static void Arm(Harmony harmony,Type hero)
        {
            heroType=hero;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(WebbedMario),nameof(BeforeState)));
            harmony.Patch(AccessTools.Method(hero,"RegainControl"),
                postfix:new HarmonyMethod(typeof(WebbedMario),nameof(AfterControl)));
        }
        internal static bool Matches(string fsm,string owner)=>fsm=="Fade"&&owner!=null&&owner.StartsWith("RestBench Spider",StringComparison.Ordinal);
        internal static int Stage(string state)
        {
            switch(state)
            {
                case "Capture End":case "Fade Up":case "Stuck":case "Struggle":case "Wiggle":case "Break":return 1;
                case "Fall":return 2;
                case "Land":return 3;
                case "Relinquish Control":return 0;
                default:return -1;
            }
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Fade")return;
            var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(owner==null||!Matches("Fade",owner.name))return;
            string state=Convert.ToString(CapAccess.Read(__instance,"Name"));
            int stage=Stage(state);
            if(stage<0)return;
            Trace("Fade entered "+state);
            if(state=="Capture End")BeastBenchMario.Captured(owner);
            try
            {
                var current=CapAccess.Read(heroType,"instance") as Component;
                if(stage==0)
                {ConversationMario.ReleaseActionHold("web");return;}
                if(stage==2||stage==3)
                {
                    if(!ConversationMario.StartAction(current,stage==2?ConversationMario.Fall:0x97,0f,false,
                        hold:stage==3?5:-1,holdControl:true,group:"web"))return;
                    Forget();actor=current.transform;phase=stage;Trace("following real Hero for "+state);return;
                }
                object variables=CapAccess.Read(fsm,"Variables");
                var proxy=CapAccess.Read(CapAccess.Call(variables,"GetFsmGameObject","Webbed Knight"),"Value") as GameObject;
                if(proxy==null)proxy=owner.transform.Find("Webbed Knight")?.gameObject;
                if(proxy==null||!ConversationMario.StartAction(current,Shock,0f,false,holdControl:true,group:"web"))
                {Trace("could not start web cover; proxy="+(proxy!=null));return;}
                if(actor!=proxy.transform)
                {
                    Forget();actor=proxy.transform;knight=proxy.GetComponent<MeshRenderer>();
                    if(knight!=null)
                    {
                        oldHidden=knight.forceRenderingOff;
                        Bounds bounds=knight.bounds;
                        localFeet=actor.InverseTransformPoint(new Vector3(bounds.center.x,bounds.min.y,actor.position.z));
                    }
                    else localFeet=Vector3.zero;
                    web=actor.Find("Webbing")?.GetComponent<SpriteRenderer>();
                    if(web!=null){oldLayer=web.sortingLayerID;oldOrder=web.sortingOrder;}
                    age=0f;
                }
                if(knight!=null)knight.forceRenderingOff=true;
                phase=stage;
            }
            catch(Exception error)
            {
                Forget();ConversationMario.ReleaseActionHold("web");ConversationMario.ReleaseActionHold("web-fall");
                Plugin.Instance?.Log.LogWarning("Mario's web pose: "+error.GetBaseException().Message);
            }
        }
        internal static ArrivalMario.Sample At(float seconds)
            =>ArrivalMario.Sample.Between(Shock,Mathf.Max(0f,seconds)*ShockFramesPerSecond%ShockFrames,ShockFrames-1);
        private static void AfterControl(object __instance)
        {
            if(phase!=3||!ReferenceEquals(__instance,CapAccess.Read(heroType,"instance")))return;
            Trace("native RegainControl; held="+CapAccess.Flag(__instance,"controlReqlinquished")
                +"; input="+CapAccess.Flag(__instance,"acceptingInput"));
            ConversationMario.ReleaseActionHold("web");
            ConversationMario.ActionControlReturned("web",__instance);
            // Keep the cover until the playable Mario has actually rendered.
            // ConversationMario.Stop owns final cleanup after that handover.
        }
        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(!Active){ConversationMario.ReleaseActionHold("web");return false;}
            if(phase!=1)return false; // Falling/landing follow the real Hero, not the web double.
            if(knight!=null)knight.forceRenderingOff=true;
            age+=Time.deltaTime;sample=At(age);return true;
        }
        internal static Vector3 Anchor
        {
            get
            {
                if(phase==1)return actor.TransformPoint(localFeet);
                var box=actor.GetComponent<BoxCollider2D>();
                return new Vector3(actor.position.x,box!=null?box.bounds.min.y:actor.position.y,actor.position.z);
            }
        }
        internal static void Cover(MeshRenderer mario)
        {
            if(phase!=1||!Active||web==null||mario==null)return;
            web.sortingLayerID=mario.sortingLayerID;web.sortingOrder=mario.sortingOrder+1;
        }
        internal static void Forget()
        {
            if(knight!=null)knight.forceRenderingOff=oldHidden;
            if(web!=null){web.sortingLayerID=oldLayer;web.sortingOrder=oldOrder;}
            actor=null;knight=null;web=null;age=0f;phase=0;localFeet=Vector3.zero;
        }
    }
}
