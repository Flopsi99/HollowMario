using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Follow the Shaman's separate cutscene actor and its physics. Only its body
    // is hidden; rewards, particles, camera, fades and wake-up input stay native.
    internal static class UpgradeKnockoutMario
    {
        private static Type heroType,animatorType;
        private static Component animator;
        private static object clip;
        private static MeshRenderer proxyBody;
        private static bool oldHidden;
        private static int phase;
        private static float startFrame,lastFrame,danceAge,feetOffset;
        private static bool soulFakeout,backRecovery;
        private static float landingAge,fakeoutAge;
        // These clips contain a built-in recovery to standing. Their final
        // frames are NOT prone poses; stop before that recovery begins.
        internal const int BackFallFrame=20,BackProneFrame=37,WakeAnimation=0xC9,WakeLastFrame=20;
        private static Collider2D bodyCollider;
        internal static bool Active=>animator!=null;
        internal static bool FollowingSoulFakeout=>soulFakeout&&Active;
        internal static bool OwnsPlayback(bool active,bool sameClip,bool playing,bool fakeout)=>active&&sameClip&&playing&&!fakeout;
        internal static bool Playing=>Active&&OwnsPlayback(animator.gameObject.activeInHierarchy,
            ReferenceEquals(CapAccess.Read(animator,"CurrentClip"),clip),CapAccess.Flag(animator,"Playing"),soulFakeout)
            &&(phase!=7||ArrivalMario.Progress(CapAccess.Number(animator,"clipTime"),0f,lastFrame)<ArrivalMario.StandFraction);
        internal static void Arm(Harmony harmony,Type hero,Type nativeAnimator)
        {
            heroType=hero;animatorType=nativeAnimator;
            harmony.Patch(AccessTools.Method(nativeAnimator,"Play",new[]{AccessTools.TypeByName("tk2dSpriteAnimationClip"),typeof(float),typeof(float)}),
                postfix:new HarmonyMethod(typeof(UpgradeKnockoutMario),nameof(AfterClip)){priority=Priority.Last});
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                postfix:new HarmonyMethod(typeof(UpgradeKnockoutMario),nameof(AfterState)));
            harmony.Patch(AccessTools.Method(hero,"RegainControl"),
                postfix:new HarmonyMethod(typeof(UpgradeKnockoutMario),nameof(AfterRegainControl)));
        }
        internal static int Phase(string name,bool active)
        {
            switch(name)
            {
                case "Collect Magical 1":case "Collect Magical 2":return 1;
                case "Collect Magical 3":case "Collect Magical 3b":return 2;
                case "Collect Magical Fall":return 3;
                case "Collect Magical Land":case "Dreamer Land":return 4;
                case "Prostrate":case "Collect Normal 2":return active?5:0;
                case "Prostrate Rise":case "Collect Normal 3":return active?6:0;
                case "GetUpToIdle":return active?7:0;
                default:return 0;
            }
        }
        internal static bool CanFollowHero(int next,bool active)=>active||next==3||next==4;
        private static bool IsProxy(Component source)
        {
            if(source.gameObject.name!="Knight Cutscene Animator")return false;
            // The Quake/Scream doubles share the Hero's library but do not have
            // the first Shaman's Check Fall FSM. Identify the actual actor family.
            var current=CapAccess.Read(heroType,"instance") as Component;
            var library=CapAccess.Read(source,"Library") as UnityEngine.Object;
            return current!=null&&library!=null&&library==(CapAccess.Read(current.GetComponent(animatorType),"Library") as UnityEngine.Object);
        }
        private static void AfterClip(object __instance,object __0,float __1,float __2)
        {
            if(soulFakeout)return; // The fake pickup has already handed back to the real body.
            int next=Phase(Convert.ToString(CapAccess.Read(__0,"name")),Active);
            if(next==0)return;
            try
            {
                var source=__instance as Component;var current=CapAccess.Read(heroType,"instance") as Component;
                if(source==null||current==null||DeathMario.Active||!ReferenceEquals(CapAccess.Read(source,"CurrentClip"),__0))return;
                bool player=source.gameObject==current.gameObject;
                if(!(player&&CanFollowHero(next,Active))&&!IsProxy(source))return;
                // Some scripted knockouts animate the real Hero instead of a
                // double. Start at the hit/fall, then keep that same back pose
                // through its native prone and get-up cues.
                bool fromBack=backRecovery||(player&&!Active&&(next==3||next==4));
                Array frames=CapAccess.Read(__0,"frames") as Array;
                float fps=__2>0f?__2:CapAccess.Number(__0,"fps");
                if(frames==null||frames.Length==0||fps<=0f)return;
                float previousDance=phase==1&&next==1?danceAge:0f;
                if(!ConversationMario.StartAction(current,(fromBack?BackAt(next,0f):At(next,0f,previousDance)).Animation,0f,false,group:"upgrade"))return;
                if(proxyBody==null&&source.gameObject!=current.gameObject)
                {
                    proxyBody=source.GetComponent<MeshRenderer>();
                    if(proxyBody!=null){oldHidden=proxyBody.forceRenderingOff;proxyBody.forceRenderingOff=true;}
                }
                animator=source;clip=__0;phase=next;danceAge=previousDance;backRecovery=fromBack;
                bodyCollider=source.GetComponent<Collider2D>();
                var heroBox=current.GetComponent<BoxCollider2D>();
                feetOffset=heroBox!=null?heroBox.bounds.min.y-current.transform.position.y:0f;
                startFrame=next==7?0f:Mathf.Clamp(__1*fps,0f,frames.Length-1f);lastFrame=frames.Length-1f;
            }
            catch(Exception error){Forget();Plugin.Instance?.Log.LogWarning("Mario's upgrade knockout: "+error.GetBaseException().Message);}
        }
        // The first Shaman moves presentation from a temporary double back to
        // the real Hero during the blackout. That transfer must not depend on
        // the old, destroyed double remaining Active.
        internal static int StatePhase(string fsm,string state)
            =>fsm=="Get Fireball"?(state=="Get"?2:state=="Fall"?3:0):fsm=="Check Fall"
                ?(state=="Land"?4:state=="Black"||state=="Wait For Input"?8:state=="Get Up"?9:0):0;
        private static void AfterState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))=="Get Quake")
            {
                if(Convert.ToString(CapAccess.Read(__instance,"Name"))=="Get")
                {
                    var fake=CapAccess.Read(fsm,"GameObject") as GameObject;
                    if(fake!=null&&IsSoulFakeout("Get Quake",fake.name))BeginFakeout();
                }
                return;
            }
            int next=StatePhase(Convert.ToString(CapAccess.Read(fsm,"Name")),Convert.ToString(CapAccess.Read(__instance,"Name")));
            if(next==0)return;
            try
            {
                var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
                var current=CapAccess.Read(heroType,"instance") as Component;
                // Hero persists across scenes; compare the cutscene's scope,
                // never its scene with the persistent player's scene.
                if(owner==null||current==null||!owner.scene.IsValid()||!owner.scene.isLoaded)return;
                if(Convert.ToString(CapAccess.Read(fsm,"Name"))=="Check Fall"&&owner.name!="Knight Cutscene Animator")return;
                Component source;
                if(next>=8)source=current.GetComponent(animatorType);
                else if(Convert.ToString(CapAccess.Read(fsm,"Name"))=="Get Fireball")
                {
                    var proxy=CapAccess.Read(CapAccess.Call(CapAccess.Read(fsm,"Variables"),"GetFsmGameObject","Cutscene Knight"),"Value") as GameObject;
                    source=proxy==null?null:proxy.GetComponent(animatorType);
                }
                else source=owner.GetComponent(animatorType);
                if(source==null||(next<8&&!IsProxy(source)))return;
                object playing=CapAccess.Read(source,"CurrentClip");
                if(playing==null)return;
                // Capture after native actions have selected the actual clip.
                AfterClip(source,playing,0f,0f);
                if(!ConversationMario.StartAction(current,At(next,0f,0f).Animation,0f,false,group:"upgrade"))return;
                animator=source;clip=playing;phase=next;danceAge=0f;
                Array frames=CapAccess.Read(playing,"frames") as Array;
                startFrame=0f;lastFrame=Mathf.Max(0,(frames?.Length??1)-1);
                bodyCollider=source.GetComponent<Collider2D>();
                var box=current.GetComponent<BoxCollider2D>();
                feetOffset=box!=null?box.bounds.min.y-current.transform.position.y:0f;
            }
            catch(Exception error){Plugin.Instance?.Log.LogWarning("Mario's Shaman phase: "+error.GetBaseException().Message);}
        }
        internal static bool IsSoulFakeout(string fsm,string owner)
            =>fsm=="Get Quake"&&(owner=="Knight Get Quake Fake"||owner=="Knight Get Quake Fake(Clone)");
        internal static ArrivalMario.Sample FakeoutPose(float age,float groundedSeconds,bool grounded,bool hurt)
        {
            if(hurt)return new ArrivalMario.Sample{Animation=0x02,Frame=0};
            if(age<.08f)return new ArrivalMario.Sample{Animation=0x74,Frame=Mathf.RoundToInt(age/.08f*8f)};
            if(age<.30f)return new ArrivalMario.Sample{Animation=0x01,Frame=Mathf.RoundToInt((age-.08f)/.22f*BackFallFrame)};
            if(!grounded)return new ArrivalMario.Sample{Animation=0x01,Frame=BackFallFrame};
            if(groundedSeconds<.08f)return new ArrivalMario.Sample{Animation=0x01,
                Frame=Mathf.RoundToInt(Mathf.Lerp(BackFallFrame,BackProneFrame,groundedSeconds/.08f))};
            // Finish this same back-fall clip's get-up, quickly. Switching to a
            // belly wake-up here would visibly flip the model onto its stomach.
            if(groundedSeconds<.26f)return new ArrivalMario.Sample{Animation=0x01,
                Frame=Mathf.RoundToInt(Mathf.Lerp(BackProneFrame,79f,(groundedSeconds-.08f)/.18f))};
            return new ArrivalMario.Sample{Animation=ConversationMario.Idle,Frame=-1};
        }
        private static void BeginFakeout()
        {
            try
            {
                var current=CapAccess.Read(heroType,"instance") as Component;
                if(current==null||!ConversationMario.StartAction(current,ConversationMario.Fall,
                    ConversationMario.SideFacing(CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight")),false,group:"upgrade"))return;
                // The fakeout disables the double without playing an ending clip.
                // Follow the real Hero through LAND HERO, the crash and recovery.
                Forget();animator=current.GetComponent(animatorType);soulFakeout=true;landingAge=fakeoutAge=0f;phase=10;
                bodyCollider=current.GetComponent<Collider2D>();
                feetOffset=bodyCollider!=null?bodyCollider.bounds.min.y-current.transform.position.y:0f;
            }
            catch(Exception error){Forget();Plugin.Instance?.Log.LogWarning("Mario's Soul Master fakeout: "+error.GetBaseException().Message);}
        }
        private static void AfterRegainControl(object __instance)
            =>ConversationMario.ActionControlReturned("upgrade",__instance);
        internal static ArrivalMario.Sample At(int stage,float progress,float danceSeconds)
        {
            progress=Mathf.Clamp01(progress);
            switch(stage)
            {
                case 1:return new ArrivalMario.Sample{Animation=0xCD,Frame=Mathf.Clamp((int)(danceSeconds*30f),0,62)};
                case 2:return progress<.25f
                    ?new ArrivalMario.Sample{Animation=0x74,Frame=Mathf.RoundToInt(progress/.25f*8f)}
                    :new ArrivalMario.Sample{Animation=0x01,Frame=Mathf.RoundToInt((progress-.25f)/.75f*BackFallFrame)};
                case 3:return new ArrivalMario.Sample{Animation=0x01,Frame=BackFallFrame};
                case 4:return new ArrivalMario.Sample{Animation=0x01,Frame=Mathf.RoundToInt(Mathf.Lerp(BackFallFrame,BackProneFrame,progress))};
                case 5:return new ArrivalMario.Sample{Animation=0x01,Frame=BackProneFrame};
                // Hold the first frame of the ORIGINAL lying wake-up clip,
                // then continue that exact clip when the host says Get Up.
                case 8:return new ArrivalMario.Sample{Animation=WakeAnimation,Frame=0};
                case 9:return new ArrivalMario.Sample{Animation=WakeAnimation,Frame=Mathf.RoundToInt(progress*WakeLastFrame)};
                case 7:return progress>=ArrivalMario.StandFraction?new ArrivalMario.Sample{Animation=ConversationMario.Idle,Frame=0}:
                    new ArrivalMario.Sample{Animation=0xC9,Frame=Mathf.RoundToInt(progress/ArrivalMario.StandFraction*20f)};
                default:return new ArrivalMario.Sample{Animation=0xC9,Frame=Mathf.RoundToInt(progress*20f)};
            }
        }
        internal static ArrivalMario.Sample BackAt(int stage,float progress)
        {
            progress=Mathf.Clamp01(progress);
            if(stage==7)return new ArrivalMario.Sample{Animation=ConversationMario.Idle,Frame=0};
            int frame=stage==3?Mathf.RoundToInt(progress*BackFallFrame):stage==4
                ?Mathf.RoundToInt(Mathf.Lerp(BackFallFrame,BackProneFrame,progress)):stage==6
                ?Mathf.RoundToInt(Mathf.Lerp(BackProneFrame,79f,progress)):BackProneFrame;
            return new ArrivalMario.Sample{Animation=0x01,Frame=frame};
        }
        internal static bool TrySample(out ArrivalMario.Sample sample)
        {
            sample=default(ArrivalMario.Sample);
            if(soulFakeout&&Active)
            {
                object flags=CapAccess.Read(CapAccess.Read(heroType,"instance"),"cState");
                bool grounded=CapAccess.Flag(flags,"onGround");
                fakeoutAge+=Time.deltaTime;
                landingAge=grounded&&fakeoutAge>=.30f?landingAge+Time.deltaTime:0f;
                sample=FakeoutPose(fakeoutAge,landingAge,grounded,CapAccess.Flag(flags,"recoiling")||CapAccess.Flag(flags,"recoilFrozen"));
                return true; // The host owns the fall, landing and control.
            }
            if(proxyBody!=null)proxyBody.forceRenderingOff=true;
            if(!Active||!animator.gameObject.activeInHierarchy||!ReferenceEquals(CapAccess.Read(animator,"CurrentClip"),clip))return false;
            if(phase==1)danceAge+=Time.deltaTime;
            float progress=ArrivalMario.Progress(CapAccess.Number(animator,"clipTime"),startFrame,lastFrame);
            sample=backRecovery?BackAt(phase,progress):At(phase,progress,danceAge);
            return true;
        }
        internal static Vector3 Anchor
        {
            get
            {
                Vector3 point=animator.transform.position;
                point.y=bodyCollider!=null&&bodyCollider.enabled&&bodyCollider.gameObject.activeInHierarchy?bodyCollider.bounds.min.y:point.y+feetOffset;
                return point;
            }
        }
        internal static void Forget()
        {
            if(proxyBody!=null)proxyBody.forceRenderingOff=oldHidden;
            proxyBody=null;animator=null;clip=null;bodyCollider=null;phase=0;danceAge=0f;soulFakeout=backRecovery=false;landingAge=fakeoutAge=0f;
        }
    }
}
