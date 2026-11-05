using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The trap bench swaps the Hero for Knight Anim before the web capture.
    // Follow that swap visually; the native bench still owns input and capture.
    internal static class BeastBenchMario
    {
        internal const string Group="beast-bench";
        internal const int SeatFrame=16,StruggleFirstFrame=2,StruggleLastFrame=6,StrugglePulses=3;
        private static Type heroType,animatorType;
        private static object capturedBench;
        private static Transform actor;
        private static MeshRenderer knight;
        private static bool oldHidden,struggling;
        private static float feetOffset,age,struggleDuration;
        internal static bool Active=>actor!=null&&actor.gameObject.activeInHierarchy;
        // Capture consumes this particular bench. Its native listeners keep
        // running after landing, so this must survive presentation cleanup.
        internal static void Captured(object bench){capturedBench=bench;}
        internal static bool CanPresent(object bench)=>!ReferenceEquals(bench,capturedBench);

        internal static void Arm(Harmony harmony,Type hero,Type animator)
        {
            heroType=hero;animatorType=animator;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(BeastBenchMario),nameof(BeforeState)));
        }
        internal static bool Matches(string fsm,string owner)
            =>fsm=="Bench Control Spider"&&owner!=null&&owner.StartsWith("RestBench Spider",StringComparison.Ordinal);
        internal static int Stage(string state)
        {
            switch(state)
            {
                case "Start Rest":case "Sit Start":case "Neutral":case "Return Left":case "Return Right":case "Set Flange":return 1;
                case "Struggle":case "Start Left":case "Start Right":return 2;
                default:return 0;
            }
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Bench Control Spider")return;
            var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(!CanPresent(owner)||owner==null||!Matches("Bench Control Spider",owner.name))return;
            string state=Convert.ToString(CapAccess.Read(__instance,"Name"));int stage=Stage(state);
            if(stage==0)return; // Left/Right keep the current short struggle; setup stays untouched.
            if(WebbedMario.OwnsSequence)
            {WebbedMario.Trace("ignored old trap-bench input: "+state);return;}
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;
                if(hero==null)return;
                var proxy=CapAccess.Read(CapAccess.Call(CapAccess.Read(fsm,"Variables"),"GetFsmGameObject","Knight Anim"),"Value") as GameObject;
                Transform target=state=="Start Rest"?hero.transform:proxy!=null?proxy.transform:null;
                if(target==null||!ConversationMario.StartAction(hero,stage==2?BenchMario.Wake:BenchMario.SitDown,0f,false,
                    holdControl:true,group:Group))return;
                if(actor!=target)
                {
                    Forget();actor=target;
                    // ConversationMario already hides the Hero. Only own the double here.
                    if(target!=hero.transform)
                    {knight=target.GetComponent<MeshRenderer>();if(knight!=null)oldHidden=knight.forceRenderingOff;}
                    var box=hero.GetComponent<BoxCollider2D>();
                    feetOffset=(box!=null?box.bounds.min.y-hero.transform.position.y:0f)+BenchMario.SeatLift;
                }
                struggling=stage==2;age=0f;struggleDuration=0f;
                if(knight!=null)knight.forceRenderingOff=true;
            }
            catch(Exception error)
            {Forget();ConversationMario.ReleaseActionHold(Group);Plugin.Instance?.Log.LogWarning("Mario's Beast's Den bench: "+error.GetBaseException().Message);}
        }
        internal static ArrivalMario.Sample At(bool struggle,float progress)
        {
            if(!struggle||progress>=1f)return new ArrivalMario.Sample{Animation=BenchMario.SitDown,Frame=SeatFrame};
            float wave=Mathf.Sin(Mathf.PI*(Mathf.Clamp01(progress)*StrugglePulses%1f));
            return ArrivalMario.Sample.Between(BenchMario.Wake,
                Mathf.Lerp(StruggleLastFrame,StruggleFirstFrame,wave),StruggleLastFrame);
        }
        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(!Active){ConversationMario.ReleaseActionHold(Group);return false;}
            if(knight!=null)knight.forceRenderingOff=true;
            if(struggling&&struggleDuration<=0f)
            {
                object clip=CapAccess.Read(actor.GetComponent(animatorType),"CurrentClip");
                var frames=CapAccess.Read(clip,"frames") as Array;float fps=CapAccess.Number(clip,"fps");
                struggleDuration=frames!=null&&fps>0f?frames.Length/fps:.5f;
            }
            age+=Time.deltaTime;sample=At(struggling,age/Mathf.Max(.05f,struggleDuration));return true;
        }
        internal static Vector3 Anchor=>actor.position+Vector3.up*feetOffset;
        internal static void Forget()
        {
            if(knight!=null)knight.forceRenderingOff=oldHidden;
            actor=null;knight=null;struggling=false;age=feetOffset=struggleDuration=0f;
        }
    }
}
