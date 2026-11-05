using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // These two scenes animate doubles, not HeroController's ordinary sprite.
    // Follow their native movement and leave the dream transition, reward,
    // Dream Nail sprite, flash, glow and particle objects entirely native.
    internal static class DreamerSequenceMario
    {
        internal const string Group="dreamer-cutscene";
        internal const int WindFlip=0xCF,WindFloat=0x15,Reach=0x67;
        internal const int FlipFrames=42,FloatFrames=15,ReachLastFrame=3;
        internal const float ReachSeconds=1f;
        private static Type heroType;
        private static Component actor;
        private static object clip;
        private static MeshRenderer knight;
        private static bool oldHidden;
        private static int phase;
        private static float age,feetOffset;
        internal static bool Active=>actor!=null&&actor.gameObject.activeInHierarchy;

        internal static int Phase(string scene,string owner,string animation)
        {
            if(scene=="RestingGrounds_04"&&(owner=="Knight Lift"||owner=="Knight Lift(Clone)")&&animation=="Dreamer Lift")return 1;
            if(!string.Equals(scene,"Dream_Nailcollection",StringComparison.OrdinalIgnoreCase)
                ||(owner!="Knight Dummy"&&owner!="Knight Dummy(Clone)"))return 0;
            return animation=="DN Get 1"?2:animation=="DN Get 2"?3:animation=="DN Get 3"?4:0;
        }

        internal static void Arm(Harmony harmony,Type hero,Type animator)
        {
            heroType=hero;
            harmony.Patch(AccessTools.Method(animator,"Play",new[]{AccessTools.TypeByName("tk2dSpriteAnimationClip"),typeof(float),typeof(float)}),
                prefix:new HarmonyMethod(typeof(DreamerSequenceMario),nameof(BeforeClip)));
        }

        private static void BeforeClip(object __instance,object __0)
        {
            var source=__instance as Component;
            if(source==null)return;
            int next=Phase(source.gameObject.scene.name,source.gameObject.name,Convert.ToString(CapAccess.Read(__0,"name")));
            if(next==0)return;
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;
                if(hero==null||!source.gameObject.scene.isLoaded||DeathMario.Active)return;
                if(!ConversationMario.StartAction(hero,At(next,0f).Animation,
                    ConversationMario.SideFacing(CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight")),false,
                    restart:true,holdControl:true,group:Group))return;
                Forget();actor=source;clip=__0;phase=next;
                knight=source.GetComponent<MeshRenderer>();
                if(knight!=null){oldHidden=knight.forceRenderingOff;knight.forceRenderingOff=true;}
                var box=hero.GetComponent<BoxCollider2D>();
                feetOffset=box!=null?box.bounds.min.y-hero.transform.position.y:0f;
            }
            catch(Exception error)
            {
                Forget();ConversationMario.ReleaseActionHold(Group);
                Plugin.Instance?.Log.LogWarning("Mario's Dreamer cutscene: "+error.GetBaseException().Message);
            }
        }

        internal static ArrivalMario.Sample At(int kind,float seconds)
        {
            seconds=Mathf.Max(0f,seconds);
            if(kind==1)
            {
                int frame=Mathf.FloorToInt(seconds*30f);
                return frame<FlipFrames?new ArrivalMario.Sample{Animation=WindFlip,Frame=frame}
                    :new ArrivalMario.Sample{Animation=WindFloat,Frame=(frame-FlipFrames)%FloatFrames};
            }
            // DN Get 1 loops a four-frame native anticipation. Use elapsed time
            // rather than that loop's clock so the reaching arm never snaps back.
            return new ArrivalMario.Sample{Animation=Reach,
                Frame=kind==2?Mathf.RoundToInt(Mathf.Clamp01(seconds/ReachSeconds)*ReachLastFrame):ReachLastFrame};
        }

        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(!Active||!ReferenceEquals(CapAccess.Read(actor,"CurrentClip"),clip))
            {ConversationMario.ReleaseActionHold(Group);Forget();return false;}
            if(knight!=null)knight.forceRenderingOff=true;
            age+=Time.deltaTime;sample=At(phase,age);return true;
        }

        internal static Vector3 Anchor=>actor.transform.position+Vector3.up*feetOffset;
        internal static void Forget()
        {
            if(knight!=null)knight.forceRenderingOff=oldHidden;
            knight=null;actor=null;clip=null;phase=0;age=feetOffset=0f;
        }
    }
}
