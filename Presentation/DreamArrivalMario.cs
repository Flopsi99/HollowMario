using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Dream Entry's separate sprite double has its own 26-frame Warp In clip.
    // Keep that actor and its completion event alive; replace only its body.
    internal static class DreamArrivalMario
    {
        internal const int LongJump=0x13,ExtendedFrame=6;
        private const float FloatHeight=.9f;
        private static Type heroType;
        private static Component actor;
        private static object clip;
        private static MeshRenderer knight;
        private static bool oldHidden;
        private static float feetOffset;
        internal static bool Active=>actor!=null&&actor.gameObject.activeInHierarchy;
        internal struct Sample {internal int Animation,Frame;internal float Height;}
        internal static bool Matches(string owner,string animation)
            =>(owner=="Knight Dream Arrival"||owner=="Knight Dream Arrival(Clone)")&&animation=="Warp In";
        internal static void Arm(Harmony harmony,Type hero,Type animator)
        {
            heroType=hero;
            harmony.Patch(AccessTools.Method(animator,"Play",new[]{AccessTools.TypeByName("tk2dSpriteAnimationClip"),typeof(float),typeof(float)}),
                prefix:new HarmonyMethod(typeof(DreamArrivalMario),nameof(BeforeClip)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(DreamArrivalMario),nameof(BeforeState)));
        }
        private static void BeforeClip(object __instance,object __0)
        {
            var source=__instance as Component;
            if(source==null||!Matches(source.gameObject.name,Convert.ToString(CapAccess.Read(__0,"name"))))return;
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;
                if(hero==null||!source.gameObject.scene.IsValid()||!source.gameObject.scene.isLoaded||DeathMario.Active)return;
                if(!ConversationMario.StartAction(hero,LongJump,
                    ConversationMario.SideFacing(CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight")),
                    false,restart:true,holdControl:true,group:"dream-arrival"))return;
                Forget();actor=source;clip=__0;knight=source.GetComponent<MeshRenderer>();
                if(knight!=null){oldHidden=knight.forceRenderingOff;knight.forceRenderingOff=true;}
                var box=hero.GetComponent<BoxCollider2D>();
                feetOffset=box!=null?box.bounds.min.y-hero.transform.position.y:0f;
            }
            catch(Exception error){Forget();ConversationMario.ReleaseActionHold("dream-arrival");Plugin.Instance?.Log.LogWarning("Mario's dream arrival: "+error.GetBaseException().Message);}
        }
        private static void BeforeState(object __instance)
        {
            if(actor==null)return;
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Control"||Convert.ToString(CapAccess.Read(__instance,"Name"))!="Return Control")return;
            var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(owner!=null&&owner.name=="Dream Entry"&&owner.scene==actor.gameObject.scene)
                ConversationMario.ReleaseActionHold("dream-arrival");
        }
        internal static Sample At(float frame)
        {
            frame=Mathf.Clamp(frame,0f,25f);
            if(frame<=7f)return new Sample{Animation=LongJump,Frame=Mathf.RoundToInt(Mathf.Min(1f,frame/4f)*ExtendedFrame),Height=FloatHeight};
            if(frame<12f)
            {
                float fall=(frame-7f)/5f;
                return new Sample{Animation=ArrivalMario.BellyLand,Frame=Mathf.RoundToInt(fall*ArrivalMario.LandLastFrame),
                    Height=FloatHeight*(1f-Mathf.SmoothStep(0f,1f,fall))};
            }
            float rise=frame<14f?Mathf.Lerp(0f,ArrivalMario.HalfRiseFrame,(frame-12f)/2f):
                frame<=20f?ArrivalMario.HalfRiseFrame:Mathf.Lerp(ArrivalMario.HalfRiseFrame,ArrivalMario.RiseLastFrame,(frame-20f)/5f);
            return new Sample{Animation=ArrivalMario.BellyRise,Frame=Mathf.RoundToInt(rise)};
        }
        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(!Active||!ReferenceEquals(CapAccess.Read(actor,"CurrentClip"),clip))
            {ConversationMario.ReleaseActionHold("dream-arrival");return false;}
            if(knight!=null)knight.forceRenderingOff=true;
            var current=At(CapAccess.Number(actor,"clipTime"));
            sample=new ArrivalMario.Sample{Animation=current.Animation,Frame=current.Frame};return true;
        }
        internal static Vector3 Anchor=>actor.transform.position+Vector3.up*(feetOffset+At(CapAccess.Number(actor,"clipTime")).Height);
        internal static void Forget()
        {if(knight!=null)knight.forceRenderingOff=oldHidden;knight=null;actor=null;clip=null;feetOffset=0f;}
    }
}
