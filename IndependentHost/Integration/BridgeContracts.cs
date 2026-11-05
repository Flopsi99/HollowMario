using System;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace MarioHollow
{
    // Adapt the independently authored companion to a single native/Unity player.
    public static class BridgeContracts
    {
        private sealed class Visibility{internal bool Hidden;}
        private static readonly ConditionalWeakTable<HeroController,Visibility> visibility=new ConditionalWeakTable<HeroController,Visibility>();
        private static Harmony hooks;
        private static Func<Collider2D,bool> supportSensor;
        private static readonly FieldInfo cameraAreaCollider=AccessTools.Field(typeof(CameraLockArea),"box2d");
        private static readonly FieldInfo areaCamera=AccessTools.Field(typeof(CameraLockArea),"cameraCtrl");
        private static Func<Collider2D,Collider2D> interactionCollider;
        private static Func<string,bool> interaction,floor;
        private static MethodInfo pushAnimation,popAnimation,hardLand;
        private static Func<float,float,int> landingFrame;
        private static FieldInfo proxyFsm;
        public static void CaptureVisibility(HeroController hero,bool hidden)
        {if(ReferenceEquals(hero,null)||visibility.TryGetValue(hero,out _))return;visibility.Add(hero,new Visibility{Hidden=hidden});}
        public static bool BaselineHidden(HeroController hero)
            =>!ReferenceEquals(hero,null)&&visibility.TryGetValue(hero,out Visibility state)&&state.Hidden;
        private static void BeforeHeroAwake(HeroController __instance)
        {var renderer=__instance.GetComponent<MeshRenderer>();CaptureVisibility(__instance,renderer!=null&&renderer.forceRenderingOff);}
        public static void Install(Assembly companion)
        {
            if(hooks!=null)return;
            hooks=new Harmony("gg.hollowmario.independent-contracts");
            try{
                Type plugin=companion.GetType("HollowMario.Plugin",true),conversation=companion.GetType("HollowMario.ConversationMario",true);
                supportSensor=(Func<Collider2D,bool>)Delegate.CreateDelegate(typeof(Func<Collider2D,bool>),AccessTools.Method(plugin,"IsFeetPad"));
                interactionCollider=(Func<Collider2D,Collider2D>)Delegate.CreateDelegate(typeof(Func<Collider2D,Collider2D>),AccessTools.Method(plugin,"InteractionCollider"));
                interaction=(Func<string,bool>)Delegate.CreateDelegate(typeof(Func<string,bool>),AccessTools.Method(conversation,"KnownInteraction"));
                floor=(Func<string,bool>)Delegate.CreateDelegate(typeof(Func<string,bool>),AccessTools.Method(companion.GetType("HollowMario.CrumblingFloorContact",true),"IsFloor"));
                var flourish=companion.GetType("HollowMario.MapFallFlourish",true);
                pushAnimation=AccessTools.Method(flourish,"PushCinematicAnimation");popAnimation=AccessTools.Method(flourish,"PopCinematicAnimation");
                landingFrame=(Func<float,float,int>)Delegate.CreateDelegate(typeof(Func<float,float,int>),AccessTools.Method(companion.GetType("HollowMario.ArrivalMario",true),"HardLandingFrame"));
                hardLand=AccessTools.Method(typeof(HeroController),"DoHardLanding");proxyFsm=AccessTools.Field(typeof(HeroController),"proxyFSM");
                hooks.Patch(hardLand,prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeHardLanding)),postfix:new HarmonyMethod(typeof(BridgeContracts),nameof(AfterHardLanding)));
                hooks.Patch(AccessTools.Method(typeof(HeroController),"Awake"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeHeroAwake)){priority=Priority.First+200});
                foreach(string phase in new[]{"Enter","Stay"})
                    hooks.Patch(AccessTools.Method(typeof(CameraLockArea),"OnTrigger"+phase+"2D"),prefix:new HarmonyMethod(typeof(BridgeContracts),phase=="Enter"?nameof(BeforeCameraEnter):nameof(BeforeCameraStay)){priority=Priority.First+200});
                hooks.Patch(AccessTools.Method(typeof(CameraLockArea),"OnTriggerExit2D"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeCameraExit)){priority=Priority.First+200});
                hooks.Patch(AccessTools.Method(companion.GetType("HollowMario.KnightResetFollow",true),"ShareMarioSpeed"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforePublishSpeed)));
                hooks.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.GetVelocity2d"),"DoGetVelocity"),postfix:new HarmonyMethod(typeof(BridgeContracts),nameof(AfterReadVelocity)));
                foreach(string phase in new[]{"Enter","Exit"})
                    hooks.Patch(AccessTools.Method(AccessTools.TypeByName("TrackTriggerObjects"),"OnTrigger"+phase+"2D"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeTrackedContact)){priority=Priority.First+200});
                hooks.Patch(AccessTools.Method(AccessTools.TypeByName("TrackTriggerObjects"),"GetOverlappedColliders"),transpiler:new HarmonyMethod(typeof(BridgeContracts),nameof(TrackedObjects)));
                foreach(string type in new[]{"Trigger2dEvent","Trigger2dEventLayer"})foreach(string phase in new[]{"Enter","Stay","Exit"})
                    hooks.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions."+type),"DoTrigger"+phase+"2D"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeTrigger)){priority=Priority.First+200});
                foreach(string phase in new[]{"Enter","Stay","Exit"})
                    hooks.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm),"OnTrigger"+phase+"2D"),prefix:new HarmonyMethod(typeof(BridgeContracts),nameof(BeforeSystemTrigger)){priority=Priority.First+200});
                MovementOwnership.Install(hooks,companion);
                CameraTrace.Install(hooks,companion,supportSensor);
            }catch{Dispose();throw;}
        }
        public static bool BeforePublishSpeed(object __0)
        {
            if(!(__0 is MarioController controller)||controller.MarioId<0)return true;
            // Rigidbody velocity actually integrates a kinematic body. Native
            // Mario already moved it; publishing that speed would move it twice.
            if(controller.HardLanding)controller.hero.hero_state=GlobalEnums.ActorStates.hard_landing;
            return false;
        }
        public static Vector2 ProxyVelocity(float x,float y)=>new Vector2(x,y)*(30f*MarioMath.Scale);
        private static void AfterReadVelocity(object __instance)
        {
            MarioController controller=MarioController.Instance;
            if(ReferenceEquals(controller,null)||controller.MarioId<0||!ReferenceEquals(Read(__instance,"component"),controller.body))return;
            Vector2 velocity=ProxyVelocity(controller.State.vx,controller.State.vy);
            if(Read(__instance,"space") is Space space&&space==Space.Self)
                velocity=controller.body.transform.InverseTransformDirection(velocity);
            WriteValue(Read(__instance,"vector"),velocity);
            WriteValue(Read(__instance,"x"),velocity.x);WriteValue(Read(__instance,"y"),velocity.y);
        }
        private static void WriteValue(object variable,object value)
        {
            if(variable==null||Read(variable,"IsNone") is bool none&&none)return;
            AccessTools.Property(variable.GetType(),"Value")?.SetValue(variable,value,null);
        }
        public static bool AllowsSupportSensor(string fsm)
            =>(interaction!=null&&interaction(fsm))||(floor!=null&&floor(fsm))
                ||fsm=="Bench Control"||fsm=="Door Control"||fsm=="Quake Floor";
        private static bool ForwardSensor(string fsm,ref Collider2D contact)
        {
            if(!AllowsSupportSensor(fsm))return false;
            contact=interactionCollider(contact);
            return contact!=null;
        }
        private static bool BeforeTrigger(object __instance,ref Collider2D __0)
        {
            if(supportSensor==null||!supportSensor(__0))return true;
            object fsm=Read(__instance,"Fsm");
            return ForwardSensor(Convert.ToString(Read(fsm,"Name")),ref __0);
        }
        private static bool BeforeSystemTrigger(object __instance,ref Collider2D __0)
        {
            if(supportSensor==null||!supportSensor(__0))return true;
            return ForwardSensor(Convert.ToString(Read(__instance,"Name")),ref __0);
        }
        private static bool BeforeTrackedContact(object __instance,ref Collider2D __0)
        {
            if(supportSensor==null||!supportSensor(__0))return true;
            if(!(__instance is Component tracker))return false;
            foreach(var fsm in tracker.GetComponents<PlayMakerFSM>())if(AllowsSupportSensor(fsm.FsmName))return ForwardSensor(fsm.FsmName,ref __0);
            return false;
        }
        private static GameObject TrackedObject(Component source)
        {
            if(source is Collider2D contact&&supportSensor!=null&&supportSensor(contact))
            {
                Collider2D body=interactionCollider(contact);
                if(body!=null)return body.gameObject;
            }
            return source.gameObject;
        }
        private static IEnumerable<CodeInstruction> TrackedObjects(IEnumerable<CodeInstruction> instructions)
        {
            // Native refresh must compare the same root object as its enter/exit
            // callbacks, including when only the child probe overlaps the range.
            foreach(var instruction in instructions)
            {
                if(instruction.operand is MethodInfo method&&method.DeclaringType==typeof(Component)&&method.Name=="get_gameObject")
                {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(BridgeContracts),nameof(TrackedObject));}
                yield return instruction;
            }
        }
        // Unity can report a trigger contact across the contact-offset gap even
        // when the proxy body is wholly above/beside a lock. Accept real XY
        // overlap only; never change a room's authored camera limits.
        private static bool BeforeCameraEnter(CameraLockArea __instance,Collider2D __0)=>CameraOverlapAllowed(__instance,__0);
        private static bool BeforeCameraStay(CameraLockArea __instance,Collider2D __0)=>CameraOverlapAllowed(__instance,__0);
        private static bool BeforeCameraExit(CameraLockArea __instance,Collider2D __0)=>CameraContactAllowed(__0);
        public static bool CameraContactAllowed(Collider2D collider)=>supportSensor==null||!supportSensor(collider);
        private static bool CameraOverlapAllowed(CameraLockArea area,Collider2D collider)
        {
            if(!CameraContactAllowed(collider))return false;
            var player=MarioController.Instance;
            if(player==null||player.MarioId<0||!ReferenceEquals(player.box,collider)||player.hero==null||player.hero.enabled)return true;
            var region=cameraAreaCollider.GetValue(area) as Collider2D;
            if(region==null||CameraBoundsOverlap(region.bounds,collider.bounds))return true;
            var camera=areaCamera.GetValue(area) as CameraController;
            // A lock may predate the handover, or have just lost real overlap.
            // Use the native exit once, preserving overlapping lock priorities.
            if(camera!=null&&camera.lockZoneList!=null&&camera.lockZoneList.Contains(area))area.OnTriggerExit2D(collider);
            return false;
        }
        public static bool CameraBoundsOverlap(Bounds region,Bounds player)
            =>player.min.x<region.max.x&&player.max.x>region.min.x
                &&player.min.y<region.max.y&&player.max.y>region.min.y;
        private static object Read(object target,string name)
        {if(target==null)return null;return AccessTools.Field(target.GetType(),name)?.GetValue(target)??AccessTools.Property(target.GetType(),name)?.GetValue(target,null);}
        public static bool Grounded(uint action)
            =>action!=0&&(action&0x800u)==0&&((action&0x1C0u)==0||(action&0x1C0u)==0x40u||(action&0x1C0u)==0x180u);
        public static bool FinishedLedgeClimb(uint before,uint after)
            =>(before==0x0000054Cu||before==0x0000054Du||before==0x0000054Fu)&&Grounded(after);
        private static bool BeforeHardLanding(HeroController __instance,out bool __state)
        {
            MarioController controller=MarioController.Instance;
            __state=controller==null||controller.MarioId<0||!ReferenceEquals(controller.hero,__instance)||Grounded(controller.State.action);
            return __state; // A proxy contact at the ledge top is not Mario landing.
        }
        private static void AfterHardLanding(HeroController __instance,bool __state)
        {
            if(!__state)return;
            MarioController controller=MarioController.Instance;
            if(controller==null||controller.MarioId<0||!ReferenceEquals(controller.hero,__instance))return;
            // Either the native contact or our simulated landing can arrive first.
            // Consume the same request so the second route cannot replay its effects.
            __instance.cState.willHardLand=false;
            controller.BeginHardLanding(__instance.HARD_LANDING_TIME);
            __instance.GetComponent<HeroAnimationController>()?.PlayClip("HardLand");
        }
        public static bool LandedOnFloor(uint before,uint after)
            =>(before&0x800u)!=0&&Grounded(after);
        public static void Landed(MarioController controller)
        {
            var hero=controller.hero;
            var support=Physics2D.Raycast(new Vector2(hero.transform.position.x,controller.box.bounds.min.y+.1f),Vector2.down,.25f,LayerMask.GetMask("Terrain","Soft Terrain"));
            bool noHardLand=support.collider!=null&&support.collider.GetComponent<NoHardLanding>()!=null;
            if(hero.cState.willHardLand&&!noHardLand&&!Convert.ToBoolean(Read(hero,"inAcid")??false)){
                hero.cState.willHardLand=false; // Consume once, before native contact callbacks.
                hardLand.Invoke(hero,null);
                HostTrace.Write("native scripted hard landing delivered once at "+hero.transform.position);
            }
            (proxyFsm?.GetValue(hero) as PlayMakerFSM)?.SendEvent("HeroCtrl-Landed");
        }
        public static int BeginLandingAnimation()=>pushAnimation==null?-1:(int)pushAnimation.Invoke(null,new object[]{0x96});
        public static int BeginTimedLanding(int id,float elapsed,float duration)
        {
            Native.sm64_set_mario_anim_frame(id,(short)(landingFrame(elapsed,duration)-1));
            return BeginLandingAnimation();
        }
        public static void EndLandingAnimation(int previous){popAnimation?.Invoke(null,new object[]{previous});}
        public static void Dispose(){hooks?.UnpatchSelf();hooks=null;CameraTrace.Dispose();}
    }
}
