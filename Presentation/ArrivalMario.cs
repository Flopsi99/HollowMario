using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Follow the host's arrival clip, including PlayFromFrame starts. This never
    // changes its duration, control handover, teleport, collision or effects.
    internal static class ArrivalMario
    {
        internal const int BellyLand=0x2C,BellyRise=0x5A;
        internal const int HardLanding=0x96; // Stop crouching: bent knees to standing.
        internal const int LandLastFrame=79,RiseLastFrame=37;
        private const float LandingFraction=.35f;
        private const int HazardFirstFrame=8,HazardLastFrame=23;
        internal const int HalfRiseFrame=22,CrouchLastFrame=5,StandLastFrame=9;
        internal const float StandFraction=5f/14f; // GetUpToIdle: five get-up frames, then looping idle.
        private static Type heroType;
        private static Component animator;
        private static object clip;
        private static string clipName;
        private static int sequence;
        private static float startFrame,lastFrame,furthest;
        internal struct Sample
        {
            internal int Animation,Frame,NextFrame;
            internal float Blend;
            internal static Sample Between(int animation,float frame,int last)
            {
                int first=Mathf.FloorToInt(frame);
                return new Sample{Animation=animation,Frame=first,NextFrame=(first+1)%(last+1),Blend=frame-first};
            }
        }

        internal static void Arm(Harmony harmony,Type hero,Type nativeAnimator)
        {
            heroType=hero;
            harmony.Patch(AccessTools.Method(nativeAnimator,"Play",new[]{AccessTools.TypeByName("tk2dSpriteAnimationClip"),typeof(float),typeof(float)}),
                prefix:new HarmonyMethod(typeof(ArrivalMario),nameof(BeforeClip)){priority=Priority.Last});
        }
        internal static int Sequence(string name,bool afterLanding)
            =>name=="Wake Up Ground"||name=="Respawn Wake"?1:name=="Super Hard Land"?2:name=="Collect Normal 3"&&afterLanding?3:name=="Hazard Respawn"?4:
                name=="DJ Get Land"?5:name=="ToProne"?6:0;
        internal static int NextSequence(string name,int previous)
        {
            if(name=="Collect Normal 2")
            {
                if(previous==5||previous==12)return 12; // Hold the landed crouch.
                if(previous==6||previous==7||previous==9)return 7; // Half-rise, then hold.
            }
            if(name=="Collect Normal 3")
            {
                if(previous==5||previous==12)return 10;
                if(previous==7)return 8;
                if(previous==6||previous==9)return 3;
            }
            if(name=="Prostrate"&&(previous==6||previous==7||previous==9))return 9;
            if(name=="Prostrate Rise"&&(previous==6||previous==7||previous==9))return 3;
            if(name=="GetUpToIdle")
            {
                if(previous==5||previous==12)return 13;
                if(previous==7)return 11;
                if(previous==6||previous==9)return 14;
            }
            return Sequence(name,previous==2);
        }
        internal static bool Continues(string name)=>sequence!=0&&NextSequence(name,sequence)!=0;
        private static void BeforeClip(object __instance,object __0,float __1,float __2)
        {
            string name=Convert.ToString(CapAccess.Read(__0,"name"));
            int next=NextSequence(name,sequence);
            if(next==0)return;
            try
            {
                var current=CapAccess.Read(heroType,"instance") as Component;
                var source=__instance as Component;
                if(current==null||source==null||source.gameObject!=current.gameObject||DeathMario.Active||UpgradeKnockoutMario.Active||WebbedMario.OwnsSequence)return;
                Array frames=CapAccess.Read(__0,"frames") as Array;
                float fps=__2>0f?__2:CapAccess.Number(__0,"fps");
                if(frames==null||frames.Length<1||fps<=0f)return;
                if(!ConversationMario.StartAction(current,At(next,0f).Animation,
                    ConversationMario.SideFacing(CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight")),false,
                    restart:true,holdControl:next==2,group:next==4?"hazard-return":"wake"))return;
                animator=source;clip=__0;clipName=name;sequence=next;
                startFrame=Mathf.Clamp(__1*fps,0f,frames.Length-1f);lastFrame=frames.Length-1f;furthest=0f;
            }
            catch(Exception error){Forget();Plugin.Instance?.Log.LogWarning("Mario's arrival pose: "+error.GetBaseException().Message);}
        }
        internal static float Progress(float current,float first,float last)
            =>Mathf.Clamp01((current-first)/Mathf.Max(1f,last-first));
        internal static int HardLandingFrame(float elapsed,float duration)
            =>Mathf.RoundToInt(Mathf.Clamp01(elapsed/Mathf.Max(1f/30f,duration-1f/30f))*StandLastFrame);
        internal static Sample At(int kind,float progress)
        {
            progress=Mathf.Clamp01(progress);
            switch(kind)
            {
                case 5:return new Sample{Animation=0x97,Frame=Mathf.RoundToInt(progress*CrouchLastFrame)};
                case 6:return new Sample{Animation=BellyLand,Frame=Mathf.RoundToInt(progress*LandLastFrame)};
                case 7:return new Sample{Animation=BellyRise,Frame=Mathf.RoundToInt(progress*HalfRiseFrame)};
                case 8:return new Sample{Animation=BellyRise,Frame=Mathf.RoundToInt(Mathf.Lerp(HalfRiseFrame,RiseLastFrame,progress))};
                case 9:return new Sample{Animation=BellyLand,Frame=LandLastFrame};
                case 10:return new Sample{Animation=0x96,Frame=Mathf.RoundToInt(progress*StandLastFrame)};
                case 12:return new Sample{Animation=0x97,Frame=CrouchLastFrame};
                case 11:case 13:case 14:
                    if(progress>=StandFraction)return new Sample{Animation=ConversationMario.Idle,Frame=0};
                    float standing=progress/StandFraction;
                    return kind==13?new Sample{Animation=0x96,Frame=Mathf.RoundToInt(standing*StandLastFrame)}:
                        new Sample{Animation=BellyRise,Frame=Mathf.RoundToInt(Mathf.Lerp(kind==11?HalfRiseFrame:0,RiseLastFrame,standing))};
            }
            // The entire native respawn is one stumble recovery. Do not append
            // a different idle/head pose before gameplay takes over.
            if(kind==4)return new Sample{Animation=ConversationMario.HazardRecovery,
                Frame=Mathf.RoundToInt(Mathf.Lerp(HazardFirstFrame,HazardLastFrame,progress))};
            bool landing=kind==2||(kind==1&&progress<LandingFraction);
            float phase=kind==1?(landing?progress/LandingFraction:(progress-LandingFraction)/(1f-LandingFraction)):progress;
            return new Sample{Animation=landing?BellyLand:BellyRise,
                Frame=Mathf.RoundToInt(phase*(landing?LandLastFrame:RiseLastFrame))};
        }
        internal static bool TrySample(out Sample sample)
        {
            sample=default(Sample);
            if(animator==null||!ReferenceEquals(CapAccess.Read(animator,"CurrentClip"),clip))return false;
            sample=At(sequence,ClockProgress());
            return true;
        }
        private static float ClockProgress()
        {
            float value=Progress(CapAccess.Number(animator,"clipTime"),sequence==11||sequence==13||sequence==14?0f:startFrame,lastFrame);
            if(sequence>=5){furthest=Mathf.Max(furthest,value);value=furthest;}
            return value;
        }
        internal static bool MotionRemaining(int kind,float progress)
            =>kind!=9&&kind!=12&&progress<(kind==11||kind==13||kind==14?StandFraction:1f);
        internal static bool Playing=>animator!=null&&ReferenceEquals(CapAccess.Read(animator,"CurrentClip"),clip)&&CapAccess.Flag(animator,"Playing")
            &&(sequence<5||MotionRemaining(sequence,ClockProgress()));
        internal static void Forget(){animator=null;clip=null;clipName=null;sequence=0;startFrame=lastFrame=furthest=0f;}
    }
}
