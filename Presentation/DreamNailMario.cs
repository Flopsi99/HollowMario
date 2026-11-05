using System;
using UnityEngine;

namespace HollowMario
{
    // Sample the real Dream Nail animation clock so Dream Wielder and cancels
    // keep their native timing. This changes only Mario's mesh pose and yaw.
    internal static class DreamNailMario
    {
        internal const int Grab=0xB5,Swing=0xB6,Release=0xB7,Hold=0xB8;
        internal const float StrikeTurnFraction=.4f;
        private static Type animatorType;
        private static object animator;
        private static int phase;
        private static bool right;
        private static float lastYaw,cancelFrom;
        internal struct Sample {internal int Animation,Frame;internal float Yaw;}
        internal static void Initialize(Type type){animatorType=type;}
        internal static int Phase(string state)
            =>state=="Start"?1:state=="Charge"||state=="Slash Antic"?2:state=="Slash"?3:state=="Charge Cancel"?4:0;
        internal static void Begin(string state,Component hero)
        {
            int next=Phase(state);
            if(next==0){Forget();return;}
            right=CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight");
            if(next==1||phase==0)lastYaw=right?Mathf.PI*.5f:-Mathf.PI*.5f;
            cancelFrom=lastYaw;phase=next;animator=hero.GetComponent(animatorType);
        }
        internal static Sample At(int stage,float progress,bool facingRight)
        {
            progress=Mathf.Clamp01(progress);
            float forward=(facingRight?1f:-1f)*Mathf.PI*.5f;
            if(stage==1)return new Sample{Animation=Grab,Frame=Mathf.RoundToInt(progress*19f),
                Yaw=Mathf.Lerp(forward,-forward,Mathf.SmoothStep(0f,1f,progress/.4f))};
            if(stage==2)return new Sample{Animation=Hold,Frame=0,Yaw=-forward};
            if(stage==3)
            {
                float turn=Mathf.SmoothStep(0f,1f,progress/StrikeTurnFraction);
                return new Sample{Animation=Swing,Frame=Mathf.RoundToInt(turn*19f),Yaw=Mathf.Lerp(-forward,forward,turn)};
            }
            return new Sample{Animation=Release,Frame=Mathf.RoundToInt(progress*39f),
                Yaw=Mathf.Lerp(-forward,forward,Mathf.SmoothStep(0f,1f,progress))};
        }
        internal static bool TrySample(out Sample sample)
        {
            sample=default(Sample);
            if(animator==null||phase==0)return false;
            object clip=CapAccess.Read(animator,"CurrentClip");
            string name=Convert.ToString(CapAccess.Read(clip,"name"));
            string expected=phase==1?"DN Start":phase==2?"DN Charge":phase==3?"DN Slash":"DN Cancel";
            if(name!=expected&&!(phase==2&&name=="DN Slash Antic"))return false;
            Array frames=CapAccess.Read(clip,"frames") as Array;
            if(frames==null||frames.Length==0)return false;
            float progress=ArrivalMario.Progress(CapAccess.Number(animator,"clipTime"),0f,frames.Length-1f);
            sample=At(phase,progress,right);
            if(phase==4)sample.Yaw=Mathf.Lerp(cancelFrom,(right?1f:-1f)*Mathf.PI*.5f,Mathf.SmoothStep(0f,1f,progress));
            lastYaw=sample.Yaw;return true;
        }
        internal static void Forget(){animator=null;phase=0;lastYaw=cancelFrom=0f;}
    }
}
