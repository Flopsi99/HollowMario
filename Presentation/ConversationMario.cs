using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The NPC still owns positioning, dialogue, choices, quest events and input.
    // Cover the native approach, read/talk pose and first rendered playable frame.
    internal static class ConversationMario
    {
        private static Type controllerType,nativeType,heroType,rendererType,mathType,animatorType,gameType;
        private static MethodInfo deactivate,takeTick,render,dispose,ownerTarget,touchingGround;
        private static FieldInfo buffers;
        private static ManualLogSource log;
        private static GameObject speaker,modelObject;
        private static Component hero;
        private static MeshRenderer knight,model;
        private static OpeningMarioPose pose;
        private static object renderer;
        private static bool requested,oldHidden,returning,background,nativeCover;
        private static bool scriptedFacing;
        private static bool transition,entering,doorWalking;
        private static int transitionGate=5;
        private static int transitionVisibility=-1;
        private static bool transitionFell,transitionHardLanding;
        private static float transitionLandingAge=-1f;
        private static bool actionPose,hazardPose,darkPose;
        private static bool actionHeld;
        private static bool challengePose;
        private static float challengeYaw;
        private static string actionGroup;
        private static float actionYaw,hazardAge;
        private static float twirlAge;
        private static float crawlX,crawlSpeed;
        private static int heldFrame=-1;
        private static string tailClip;
        private static Component tailAnimator,heroAnimator;
        private static float tailYaw;
        private static Vector3 hazardAnchor;
        private static MaterialPropertyBlock poseColors;
        private static int animation;
        private static Vector3 renderedFeet;
        private static float accumulator;
        private static int warnings;
        internal const int LookUp=0x1E,LookDown=0x1F,Walk=0x48,Idle=0xC5,Jump=0x4D,Fall=0x56;
        internal const int HazardRecovery=0x74;
        internal const int PickupDance=0xCD,PickupDanceLastFrame=62;
        internal const int DreamTwirl=0x94;
        internal const float DreamTwirlSpeed=4f*Mathf.PI; // Two turns per second; presentation only.
        internal const float UnnCrawlAnimationRate=3f;
        internal static string Context=>requested?(actionGroup??(transition?"room transition":"interaction")):"gameplay";
        internal static bool Presenting=>pose!=null;

        internal static void Arm(Harmony harmony,Type controller,Type native,ManualLogSource logger)
        {
            controllerType=controller;nativeType=native;log=logger;
            heroType=AccessTools.TypeByName("HeroController");
            touchingGround=AccessTools.Method(heroType,"CheckTouchingGround");
            gameType=AccessTools.TypeByName("GameManager");
            ownerTarget=AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Fsm"),"GetOwnerDefaultTarget");
            rendererType=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            mathType=controller.Assembly.GetType("MarioHollow.MarioMath",true);
            MarioReflection.Arm(harmony,controller);
            DarknessMario.Arm(harmony,controller);
            deactivate=AccessTools.Method(controller,"Deactivate");
            buffers=AccessTools.Field(rendererType,"Buffers");
            takeTick=AccessTools.Method(rendererType,"TakeTick");
            render=AccessTools.Method(rendererType,"Render");dispose=AccessTools.Method(rendererType,"Dispose");
            foreach(string action in new[]{"Tk2dPlayAnimation","Tk2dPlayAnimationWithEvents"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions."+action),"OnEnter"),
                    prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeAnimation)));
            harmony.Patch(AccessTools.Method(controller,"Update"),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeUpdate)));
            foreach(string method in new[]{"RelinquishControl","IgnoreInput","IgnoreInputWithoutReset"})
                harmony.Patch(AccessTools.Method(heroType,method),prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeNativeControl)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.CallMethodProper"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeControlCall)));
            harmony.Patch(render,postfix:new HarmonyMethod(typeof(ConversationMario),nameof(AfterPlayableRender)));
            harmony.Patch(AccessTools.Method(heroType,"LeaveScene"),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeLeaveScene)));
            harmony.Patch(AccessTools.Method(heroType,"EnterScene"),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeEnterScene)));
            foreach(string face in new[]{"FaceLeft","FaceRight"})
                harmony.Patch(AccessTools.Method(heroType,face),postfix:new HarmonyMethod(typeof(ConversationMario),nameof(AfterScriptedFace)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HeroAnimationController"),"PlayClip"),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeHeroClip)));
            animatorType=AccessTools.TypeByName("tk2dSpriteAnimator");
            DreamNailMario.Initialize(animatorType);
            harmony.Patch(AccessTools.Method(animatorType,"Play",new[]{AccessTools.TypeByName("tk2dSpriteAnimationClip"),typeof(float),typeof(float)}),
                prefix:new HarmonyMethod(typeof(ConversationMario),nameof(BeforeNativeClip)));
            ArrivalMario.Arm(harmony,heroType,animatorType);
            UpgradeKnockoutMario.Arm(harmony,heroType,animatorType);
            MonarchWingsMario.Arm(harmony,heroType);
            WebbedMario.Arm(harmony,heroType);
            DreamArrivalMario.Arm(harmony,heroType,animatorType);
            DreamerSequenceMario.Arm(harmony,heroType,animatorType);
            BeastBenchMario.Arm(harmony,heroType,animatorType);
            ShadePoolMario.Arm(harmony,heroType,animatorType);
            BirthplaceMario.Arm(harmony,heroType,animatorType);
        }
        internal static float Facing(float speakerX,float heroX,bool fallbackRight)
            =>(Mathf.Abs(speakerX-heroX)<.05f?fallbackRight:speakerX>heroX)?Mathf.PI*.25f:-Mathf.PI*.25f;
        internal static float PickupFacing(bool right)=>0f;
        internal static float MapFacing(bool right)=>(right?75f:-75f)*Mathf.Deg2Rad;
        internal static float ChallengeFacing(bool right)=>(right?165f:-165f)*Mathf.Deg2Rad;
        internal static float SideFacing(bool right,bool away=false)=>(right?1f:-1f)*(away?105f:75f)*Mathf.Deg2Rad;
        internal static float WalkRate(int clip,float speed)
            =>clip==SpellMario.Crawl?UnnCrawlAnimationRate:clip==Walk?Mathf.Clamp(Mathf.Abs(speed)/2.5f,1f,4f):clip==0x18?Mathf.Clamp(Mathf.Abs(speed)/1.25f,1f,4f):1f;
        internal static bool KnownInteraction(string name)
            =>name=="npc_control"||name=="Conversation Control"||name=="Shop Region"
                ||name=="Inspection"||name=="inspect"||name=="Stag Bell"||name=="Stag Control";
        internal static int PoseForClip(string clip)
        {
            if(ReturnToIdleClip(clip))return clip.StartsWith("Look",StringComparison.Ordinal)||clip=="TurnFromBG"?LookDown:Idle;
            switch(clip)
            {
                case "LookUp":case "TurnToBG":return LookUp;
                case "LookUpEnd":case "LookDownEnd":case "TurnFromBG":return LookDown;
                case "Walk":return Walk;
                case "Idle":case "Lantern Idle":case "Idle Wind":case "Idle Hurt":case "Turn":case "LookDown":return Idle;
                default:return -1;
            }
        }
        internal static bool ReturnToIdleClip(string clip)=>clip!=null&&(clip=="LookUpEnd"||clip=="LookDownEnd"||clip=="TurnFromBG"
            ||clip.EndsWith("ToIdle",StringComparison.Ordinal)||clip.EndsWith("To Idle",StringComparison.Ordinal));
        internal static bool PickupClip(string clip)=>clip!=null&&clip.StartsWith("Collect ",StringComparison.Ordinal)&&!ReturnToIdleClip(clip);
        private static bool ScriptedPickup(string clip)
            =>DeathMario.Active||UpgradeKnockoutMario.Active||MonarchWingsMario.OwnsSequence||DreamerSequenceMario.Active||BirthplaceMario.OwnsSequence
                ||ShadePoolMario.Active||BeastBenchMario.Active||WebbedMario.OwnsSequence
                ||(actionGroup=="wake"&&ArrivalMario.Continues(clip));
        internal static bool CoverNativeInteraction(bool hasView,bool isReturning,bool sensitive,bool marioActive,bool controlHeld,bool returningClip,bool playing)
        {
            if(sensitive||(marioActive&&!controlHeld&&(!hasView||isReturning)))return false;
            return hasView||(playing&&(controlHeld||(returningClip&&!marioActive)));
        }
        private static bool InGameplay=>Convert.ToInt32(CapAccess.Read(CapAccess.Read(gameType,"UnsafeInstance"),"gameState"))==4;
        internal static bool CoverControlLock(bool wanted,bool playing,bool owned,bool special,bool dead,bool held,bool accepts)
            =>wanted&&playing&&!owned&&!special&&!dead&&(held||!accepts);
        private static void BeforeNativeControl(object __instance)
        {
            if(requested&&!returning)return;
            try
            {
                object controller=CapAccess.Read(controllerType,"Instance");
                if(!CapAccess.Flag(controller,"Wanted")||!ReferenceEquals(__instance,CapAccess.Read(heroType,"instance")))return;
                CoverLockedHero(controller,__instance as Component,true);
            }
            catch(Exception error){Stop();Warn(error);}
        }
        private static void ObserveNativeControl(object controller)
        {
            if((requested&&!returning)||!CapAccess.Flag(controller,"Wanted")
                ||Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)return;
            try{CoverLockedHero(controller,CapAccess.Read(heroType,"instance") as Component,false);}
            catch(Exception error){Stop();Warn(error);}
        }
        private static void CoverLockedHero(object controller,Component current,bool takingControl)
        {
            if(current==null)return;
            object state=CapAccess.Read(current,"cState");
            if(!CoverControlLock(CapAccess.Flag(controller,"Wanted"),InGameplay,requested&&!returning,
                OpeningMario.OwnsSequence||BenchMario.Presenting||DeathMario.Active,
                CapAccess.Flag(state,"dead"),takingControl||CapAccess.Flag(current,"controlReqlinquished"),CapAccess.Flag(current,"acceptingInput")))return;
            if(!Begin(current.gameObject))return;
            nativeCover=true;
            log?.LogInfo("Mario covers an unassigned native control lock; the game's script keeps its own physics and timing.");
        }
        internal static int NativeControlAnimation(string clip,bool grounded,float vx,float vy)
        {
            if(clip!=null&&clip.StartsWith("Roar",StringComparison.Ordinal))return grounded?0x74:0x02;
            // A cutscene's stale Walk/Idle clip must not make an airborne entry
            // walk in place. Follow the real body until a specific pose takes over.
            if(!grounded)return vy>.05f?Jump:Fall;
            if(clip=="HardLand")return ArrivalMario.HardLanding;
            if(clip=="Land")return 0x57;
            return Mathf.Abs(vx)>.05f?Walk:Idle;
        }
        private static void AfterScriptedFace(object __instance)
        {
            if(requested&&!actionPose&&!transition&&ReferenceEquals(__instance,CapAccess.Read(heroType,"instance")))scriptedFacing=true;
        }
        private static bool TargetsHero(object action,object fsm,Component current)
            =>current!=null&&ReferenceEquals(ownerTarget.Invoke(fsm,new[]{CapAccess.Read(action,"gameObject")}),current.gameObject);
        private static bool HasInteractionPose(object fsm,Component current)
        {
            if(KnownInteraction(Convert.ToString(CapAccess.Read(fsm,"Name"))))return true;
            Array states=CapAccess.Read(fsm,"States") as Array;
            if(states==null)return false;
            foreach(object state in states)
            {
                Array actions=CapAccess.Read(state,"Actions") as Array;
                if(actions==null)continue;
                foreach(object action in actions)
                {
                    string type=action?.GetType().Name;
                    if(type!="Tk2dPlayAnimation"&&type!="Tk2dPlayAnimationWithEvents")continue;
                    string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(action,"clipName"),"Value"));
                    if((clip=="LookUp"||clip=="LookDown"||clip=="TurnToBG"||ReturnToIdleClip(clip)||clip.StartsWith("Collect ",StringComparison.Ordinal))&&TargetsHero(action,fsm,current))return true;
                }
            }
            return false;
        }
        private static bool Begin(GameObject owner)
        {
            if(WebbedMario.OwnsSequence)return false;
            object controller=CapAccess.Read(controllerType,"Instance");
            if(!CapAccess.Flag(controller,"Wanted")||OpeningMario.OwnsSequence||BenchMario.Presenting||DeathMario.Active)return false;
            if(!requested||speaker!=owner||returning)
            {Stop();speaker=owner;requested=true;animation=Idle;}
            return true;
        }
        internal static bool StartAction(Component current,int clip,float yaw,bool dark,int hold=-1,bool restart=false,bool hazard=false,
            bool holdControl=false,string group=null)
        {
            // The original trap bench continues listening while its Fade FSM
            // runs the web escape. Its old input-driven poses must not steal
            // this presentation and restore the hidden double between struggles.
            if(!WebbedMario.AllowsPresentation(group,hazard))
            {WebbedMario.Trace("blocked competing pose "+(group??"interaction"));return false;}
            object controller=CapAccess.Read(controllerType,"Instance");
            if(current==null||!ReferenceEquals(current,CapAccess.Read(heroType,"instance"))
                ||!CapAccess.Flag(controller,"Wanted")||OpeningMario.OwnsSequence||BenchMario.Presenting
                ||(DeathMario.Active&&group!="death"))return false;
            // A lingering cast/cancel FSM event must not take the actor back
            // after an accepted hit has already switched to native Mario recoil.
            object damageFlags=CapAccess.Read(current,"cState");
            if(DamageCancellable(group)&&(CapAccess.Flag(controller,"DamagePending")
                ||CapAccess.Flag(damageFlags,"recoiling")||CapAccess.Flag(damageFlags,"recoilFrozen")))return false;
            if(!requested||!actionPose||hazardPose!=hazard||speaker!=current.gameObject||returning)
            {
                Stop();speaker=current.gameObject;requested=actionPose=true;hazardPose=hazard;
                var box=current.GetComponent<BoxCollider2D>();
                hazardAnchor=new Vector3(current.transform.position.x,box!=null?box.bounds.min.y:current.transform.position.y,current.transform.position.z);
            }
            nativeCover=false;
            if(clip!=animation||group!=actionGroup)
            {twirlAge=0f;crawlX=current.transform.position.x;crawlSpeed=0f;}
            animation=clip;actionYaw=yaw;darkPose=dark;heldFrame=hold;actionHeld=holdControl;actionGroup=group;
            if(group!="wake"&&group!="hazard-return")ArrivalMario.Forget();
            if(group!="upgrade")UpgradeKnockoutMario.Forget();
            if(group!=MonarchWingsMario.Group)MonarchWingsMario.Forget();
            if(group!="dream")DreamNailMario.Forget();
            if(group!="web")WebbedMario.Forget();
            if(group!="dream-arrival")DreamArrivalMario.Forget();
            if(group!=DreamerSequenceMario.Group)DreamerSequenceMario.Forget();
            if(group!=BeastBenchMario.Group)BeastBenchMario.Forget();
            if(group!=ShadePoolMario.Group)ShadePoolMario.Forget();
            if(group!=BirthplaceMario.Group)BirthplaceMario.Forget();
            if(restart&&pose!=null)pose.SetAnimationFrame(-1);
            if(group=="map")KnightFocus.MapPresentation(controller,current);
            if(Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)deactivate.Invoke(controller,null);
            return true;
        }
        internal static bool DamageCancellable(string group)
            =>group=="fireballLevel"||group=="quakeLevel"||group=="screamLevel"||group=="focus";
        internal static bool CanInterruptForDamage(object current)
            =>requested&&actionPose&&DamageCancellable(actionGroup)
                &&ReferenceEquals(current,CapAccess.Read(heroType,"instance"));
        internal static bool InterruptForDamage(object current)
        {
            if(!ReferenceEquals(current,CapAccess.Read(heroType,"instance"))||DeathMario.Active)return false;
            object flags=CapAccess.Read(current,"cState");
            if(CapAccess.Flag(flags,"dead")||CapAccess.Flag(flags,"hazardDeath")
                ||CapAccess.Flag(flags,"hazardRespawning")||CapAccess.Flag(flags,"transitioning"))return false;
            if(requested)
            {
                if(!actionPose||!DamageCancellable(actionGroup))return false;
                log?.LogInfo("Mario damage takes over interrupted "+actionGroup+"; holding the last mesh until gameplay draws.");
                actionHeld=false;ReleaseForGameplay();
            }
            return true;
        }
        internal static bool IsHazardPose=>requested&&hazardPose;
        private static bool HazardPresentation=>requested&&(hazardPose||(actionPose&&actionGroup=="hazard-return"));
        private static void ReleaseForGameplay()
        {
            // Keep the last mesh and Knight hiding until gameplay really draws.
            try{pose?.Dispose();}
            finally{pose=null;returning=true;ArrivalMario.Forget();}
        }
        internal static void ReleaseActionHold(string group)
        {if(actionPose&&actionGroup==group)actionHeld=false;}
        internal static void ActionControlReturned(string group,object current)
        {
            if(!requested||returning||!actionPose||actionGroup!=group||!ReferenceEquals(current,CapAccess.Read(heroType,"instance")))return;
            object flags=CapAccess.Read(current,"cState");
            if(!CapAccess.Flag(current,"controlReqlinquished")&&CapAccess.Flag(current,"acceptingInput")
                &&!CapAccess.Flag(flags,"dead")&&!CapAccess.Flag(flags,"hazardDeath")&&!CapAccess.Flag(flags,"hazardRespawning")
                &&!CapAccess.Flag(flags,"transitioning"))ReleaseForGameplay();
        }
        internal static float HazardFade(float age)=>1f-Mathf.Clamp01(age/.6f);
        private static void BeginTransition(Component current,int gate,bool entry,bool walkDoor)
        {
            object controller=CapAccess.Read(controllerType,"Instance");
            if(current==null||!ReferenceEquals(current,CapAccess.Read(heroType,"instance"))
                ||!CapAccess.Flag(controller,"Wanted")||OpeningMario.OwnsSequence||BenchMario.Presenting||DeathMario.Active)return;
            if(!requested||!transition||speaker!=current.gameObject||returning)
            {Stop();speaker=current.gameObject;requested=transition=true;}
            transitionGate=gate;entering=entry;doorWalking=walkDoor;
            // Let the host regain its dynamic body before it sets transition
            // velocities. The native gate/door routine alone moves that body.
            if(Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)deactivate.Invoke(controller,null);
        }
        private static void BeforeLeaveScene(object __instance,object __0)
        {
            try{BeginTransition(__instance as Component,__0==null?(transition?transitionGate:5):Convert.ToInt32(__0),false,
                transition&&transitionGate==4);}
            catch(Exception error){Stop();Warn(error);}
        }
        private static void BeforeEnterScene(object __instance,object __0)
        {
            try{if(__0!=null)BeginTransition(__instance as Component,Convert.ToInt32(CapAccess.Call(__0,"GetGatePosition")),true,false);}
            catch(Exception error){Stop();Warn(error);}
        }
        private static void BeforeHeroClip(object __instance,string __0)
        {
            if(__0!="Exit Door To Idle")return;
            try{BeginTransition(CapAccess.Read(__instance,"heroCtrl") as Component,4,true,true);}
            catch(Exception error){Stop();Warn(error);}
        }
        private static void ObserveTransition(object controller)
        {
            // Scene-load callbacks can dispose the outgoing view after EnterScene
            // has already been called. The actual native state re-arms entry.
            if(transition||DeathMario.Active||controller==null||(actionPose&&(actionGroup=="wake"||actionGroup=="upgrade"||actionGroup=="hazard-return"||actionGroup=="web"||actionGroup=="web-fall"||actionGroup=="dream-arrival"||actionGroup==DreamerSequenceMario.Group||actionGroup==BirthplaceMario.Group)))return;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            if(current==null||!CapAccess.Flag(CapAccess.Read(current,"cState"),"transitioning"))return;
            int state=Convert.ToInt32(CapAccess.Read(current,"transitionState"));
            BeginTransition(current,Convert.ToInt32(CapAccess.Read(current,"gatePosition")),state!=1,false);
        }
        internal static int TransitionAnimation(int gate,bool entry,bool walkDoor,bool grounded,float vx,float vy)
        {
            if(gate==4)return walkDoor?Walk:Idle;
            if(!grounded)
            {
                bool rise=vy>.05f||(Mathf.Abs(vy)<=.05f&&((entry&&gate==3)||(!entry&&gate==0)));
                return rise?Jump:Fall;
            }
            return Mathf.Abs(vx)>.05f?Walk:Idle;
        }
        internal static int TransitionLanding(int normal,bool grounded,bool hard,bool soft)
            =>!grounded?normal:hard?ArrivalMario.HardLanding:soft?0x57:normal;
        internal static bool TransitionGrounded(bool onGround,bool touching,float vy)
            =>onGround||(touching&&Mathf.Abs(vy)<.1f);
        internal static int EntryLanding(int normal,bool entering,bool grounded,bool fell,bool hard,float age,float duration)
            =>entering&&grounded&&fell&&age>=0f&&age<duration?(hard?ArrivalMario.HardLanding:0x57):normal;
        internal static bool TransitionVisible(bool entry,bool waiting,bool knightVisible)
            =>!entry||!waiting||knightVisible;
        internal static float TransitionFacing(int gate,bool entry,float vx,bool right)
            =>gate==4?(entry?0f:Mathf.PI):(Mathf.Abs(vx)>.05f?vx>0:right)?Mathf.PI*.5f:-Mathf.PI*.5f;
        internal static int TransitionAction(int normal,bool hurt,bool crystal)
            =>hurt?0x02:crystal?0x8C:normal;
        internal static float TransitionHurtFacing(float vx,bool right)
            =>(Mathf.Abs(vx)>.05f?vx<0f:right)?Mathf.PI*.5f:-Mathf.PI*.5f;
        private static void BeforeControlCall(object __instance)
        {
            if(Convert.ToString(CapAccess.Read(CapAccess.Read(__instance,"methodName"),"Value"))!="RelinquishControl"
                ||Convert.ToString(CapAccess.Read(CapAccess.Read(__instance,"behaviour"),"Value"))!="HeroController")return;
            try
            {
                object fsm=CapAccess.Read(__instance,"Fsm");
                Component current=CapAccess.Read(heroType,"instance") as Component;
                if(!(CapAccess.Read(fsm,"GameObject") is GameObject owner)||current==null||owner==current.gameObject
                    ||!TargetsHero(__instance,fsm,current))return;
                if(Convert.ToString(CapAccess.Read(fsm,"Name"))=="Door Control")
                {BeginTransition(current,4,false,true);return;}
                if(!HasInteractionPose(fsm,current))return;
                Begin(owner); // Before positioning starts, without changing any native action.
            }
            catch(Exception error){Stop();Warn(error);}
        }

        private static void BeforeAnimation(object __instance)
        {
            try
            {
                object fsm=CapAccess.Read(__instance,"Fsm");
                if(!(CapAccess.Read(fsm,"GameObject") is GameObject owner))return;
                Component current=CapAccess.Read(heroType,"instance") as Component;
                if(current==null||!TargetsHero(__instance,fsm,current))return;
                string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(__instance,"clipName"),"Value"));
                if(PickupClip(clip))
                {
                    if(!ScriptedPickup(clip)&&Convert.ToString(CapAccess.Read(fsm,"Name"))!="Dream Return")Pickup(current);
                    return;
                }
                if(owner==current.gameObject)return;
                if(clip=="Challenge Start")
                {
                    if(Begin(owner))
                    {
                        challengePose=true;animation=0x67;heldFrame=4;
                        challengeYaw=ChallengeFacing(CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight"));
                    }
                    return;
                }
                if(challengePose&&clip=="Challenge End")
                {animation=Idle;heldFrame=-1;return;}
                if(clip=="LookUp"||clip=="LookDown"||clip=="TurnToBG")
                {
                    if(!Begin(owner))return;
                }
                if(requested&&speaker==owner)
                {
                    int next=PoseForClip(clip);
                    if(next>=0)
                    {
                        animation=next;
                        if(clip=="TurnToBG")background=true;
                        if(clip=="Turn"||clip=="TurnToIdle")scriptedFacing=true;
                    }
                    // Keep an owned conversation covered between clip variants.
                    // Dedicated pickup/spell/transition handlers can still take
                    // ownership; an unfamiliar NPC clip must not reveal the Knight.
                    else if(!TailPlaying())animation=Idle;
                }
            }
            catch(Exception error)
            {Stop();Warn(error);}
        }
        private static void Pickup(Component current)
        {
            // Every ordinary collection family shares the same celebration.
            // Story actors retain their dedicated knockout/arrival sequences.
            StartAction(current,PickupDance,PickupFacing(CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight")),
                false,PickupDanceLastFrame,false,false,false,"pickup");
        }
        private static void BeforeNativeClip(object __instance,object __0)
        {
            try
            {
                string clip=Convert.ToString(CapAccess.Read(__0,"name"));
                bool tail=ReturnToIdleClip(clip),pickup=PickupClip(clip);
                if(PoseForClip(clip)<0&&!pickup)return;
                if(DreamerSequenceMario.Active)return;
                if(BeastBenchMario.Active||WebbedMario.OwnsSequence)return;
                if(ShadePoolMario.Active)return;
                if(BirthplaceMario.OwnsSequence)return;
                var current=CapAccess.Read(heroType,"instance") as Component;
                var animator=__instance as Component;
                if(current==null||animator==null||animator.gameObject!=current.gameObject||DeathMario.Active)return;
                if(pickup)
                {
                    if(!ScriptedPickup(clip))Pickup(current);
                    return;
                }
                object controller=CapAccess.Read(controllerType,"Instance");
                bool live=Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0;
                bool held=CapAccess.Flag(current,"controlReqlinquished")||!CapAccess.Flag(current,"acceptingInput");
                bool sensitive=transition||(actionPose&&actionGroup!="head-return"&&!(actionGroup=="pickup"&&tail));
                if(!CoverNativeInteraction(requested,returning,sensitive,live,held,tail,requested||InGameplay))return;
                if(!requested&&!Begin(current.gameObject))return;
                bool headPose=clip=="LookUp"||clip=="LookDown"||clip=="TurnToBG"||clip=="TurnFromBG"
                    ||(tail&&clip.StartsWith("Look",StringComparison.Ordinal));
                if(nativeCover&&!headPose)return; // Keep live walk/fall sampling through generic Idle/Turn tails.
                if(headPose)nativeCover=false;
                // Native Idle may arrive before input is restored. Retain the
                // closing pose/facing instead of briefly reopening an NPC stance.
                if(!tail&&tailClip!=null&&!actionPose&&!transition&&clip=="Idle")return;
                // One shared path for actual Hero clips, regardless of which
                // NPC/script requested them or which Play/PlayFromFrame overload.
                actionPose=false;actionHeld=false;actionGroup=null;heldFrame=-1;
                returning=false;animation=PoseForClip(clip);
                if(clip=="TurnToBG")background=true;
                else if(!tail&&(clip=="LookUp"||clip=="LookDown"||clip=="Turn"))background=false;
                if(clip=="Turn"||clip=="TurnToIdle")scriptedFacing=true;
                if(tail){ContinueHeadReturn(clip);tailClip=clip;tailAnimator=animator;}
                else{tailClip=null;tailAnimator=null;}
            }
            catch(Exception error){Stop();Warn(error);}
        }
        private static bool TailPlaying()
            =>tailAnimator!=null&&CapAccess.Flag(tailAnimator,"Playing")
                &&Convert.ToString(CapAccess.Read(CapAccess.Read(tailAnimator,"CurrentClip"),"name"))==tailClip;
        private static bool ContinueHeadReturn(string clip)
        {
            if(!requested||actionPose||transition)return false;
            // Keep the same actor, speaker and hidden Knight through the final nod.
            // Rebuilding here briefly restores the Knight and discards the look pose.
            returning=false;animation=PoseForClip(clip);heldFrame=-1;
            if(tailClip!=clip)
            {
                tailYaw=pose!=null?CapAccess.Number(pose.State,"faceAngle"):(background?Mathf.PI:0f);
                pose?.SetAnimationFrame(-1);
            }
            return true;
        }
        internal static float ClosingFacing(float start,bool right,float progress)
            =>start+Mathf.DeltaAngle(start*Mathf.Rad2Deg,(right?90f:-90f))*Mathf.Deg2Rad*Mathf.SmoothStep(0f,1f,progress);
        private static float ClosingProgress()
        {
            if(!TailPlaying())return 1f;
            var frames=CapAccess.Read(CapAccess.Read(tailAnimator,"CurrentClip"),"frames") as Array;
            return ArrivalMario.Progress(CapAccess.Number(tailAnimator,"clipTime"),0f,frames!=null?frames.Length-1f:1f);
        }
        private static void ObserveHeadReturn(object controller)
        {
            if(requested||controller==null||!CapAccess.Flag(controller,"Wanted")
                ||Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)return;
            if(!InGameplay)return;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            if(current==null||DeathMario.Active||OpeningMario.Presenting||BenchMario.Presenting)return;
            Component animator=current.GetComponent(animatorType);
            string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(animator,"CurrentClip"),"name"));
            if(!ReturnToIdleClip(clip)||!CapAccess.Flag(animator,"Playing")||!InGameplay)return;
            if(Begin(current.gameObject)&&ContinueHeadReturn(clip))
            {tailClip=clip;tailAnimator=animator;}
        }
        private static bool BeforeUpdate(object __instance)
        {
            if(DeathMario.Finished()){Stop();return true;}
            ObserveTransition(__instance);
            ObserveHeadReturn(__instance);
            ObserveNativeControl(__instance);
            if(!requested)return true;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            if(!Valid(__instance)){Stop();return true;}
            if(returning)return true;
            if(!actionHeld&&CapAccess.Flag(__instance,"GameplayReady"))
            {
                // Release the native actor for gameplay to reuse, but retain
                // its last visible mesh until the playable renderer really draws.
                ReleaseForGameplay();return true;
            }
            // Menu/dialogue input belongs to Hollow Knight, never the private
            // presentation actor or the ordinary Mario controller.
            return false;
        }
        private static bool Valid(object controller)
        {
            if(speaker==null||!speaker.scene.isLoaded
                ||!CapAccess.Flag(controller,"Wanted")||OpeningMario.Presenting||BenchMario.Presenting)return false;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            object state=CapAccess.Read(current,"cState");
            return current!=null&&(DeathMario.Active||!CapAccess.Flag(state,"dead"))
                &&(DeathMario.Active||hazardPose||!CapAccess.Flag(state,"hazardDeath"))
                &&(DeathMario.Active||transition||hazardPose||actionGroup=="wake"||actionGroup=="upgrade"||actionGroup==MonarchWingsMario.Group||actionGroup=="web"||actionGroup=="web-fall"||actionGroup=="dream-arrival"||actionGroup==DreamerSequenceMario.Group||actionGroup==BirthplaceMario.Group||!CapAccess.Flag(state,"transitioning"))
                &&(returning||nativeCover||DeathMario.Active||transition||HazardPresentation||actionGroup=="roar"||actionGroup=="web"||UpgradeKnockoutMario.FollowingSoulFakeout||!CapAccess.Flag(state,"recoiling"));
        }
        private static void AfterPlayableRender(object __instance)
        {
            if(!returning||!ReferenceEquals(__instance,CapAccess.Read(CapAccess.Read(controllerType,"Instance"),"Renderer")))return;
            object triangles=CapAccess.Read(__instance,"Triangles");
            if(triangles!=null&&Convert.ToInt32(triangles)<=0)return;
            oldHidden=true; // Gameplay owns hiding the Knight now; do not reveal him in Stop.
            Stop();
        }
        private static float PoseFacing()
        {
            if(challengePose)return challengeYaw;
            if(actionPose&&actionGroup=="map")return MapFacing(CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight"));
            if(actionPose&&actionGroup=="dream"&&animation==DreamTwirl)return actionYaw+twirlAge*DreamTwirlSpeed;
            if(actionPose&&actionGroup=="focus"&&animation==SpellMario.Crawl)
                return Mathf.Abs(crawlSpeed)>=.05f?SideFacing(crawlSpeed>0f):actionYaw;
            if(actionPose&&actionGroup=="hazard-return")
                return CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight")?Mathf.PI*.5f:-Mathf.PI*.5f;
            if(actionPose)return actionYaw;
            if(nativeCover)
            {
                var body=hero.GetComponent<Rigidbody2D>();Vector2 velocity=body==null?Vector2.zero:body.velocity;
                object state=CapAccess.Read(hero,"cState");
                string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(heroAnimator,"CurrentClip"),"name"));
                animation=NativeControlAnimation(clip,CapAccess.Flag(state,"onGround"),velocity.x,velocity.y);
                heldFrame=clip.StartsWith("Roar",StringComparison.Ordinal)?4:-1;
                return TransitionFacing(5,false,velocity.x,CapAccess.Flag(state,"facingRight"));
            }
            if(transition)
            {
                var body=hero.GetComponent<Rigidbody2D>();
                Vector2 velocity=body==null?Vector2.zero:body.velocity;
                object state=CapAccess.Read(hero,"cState");
                bool right=CapAccess.Flag(state,"facingRight");
                bool frozen=CapAccess.Flag(state,"recoilFrozen"),hurt=frozen||CapAccess.Flag(state,"recoiling");
                bool grounded=CapAccess.Flag(state,"onGround");
                if(!grounded&&Mathf.Abs(velocity.y)<.1f)
                    grounded=TransitionGrounded(false,touchingGround?.Invoke(hero,null) is bool contact&&contact,velocity.y);
                if(!grounded&&velocity.y<-.05f)transitionFell=true;
                animation=TransitionAnimation(transitionGate,entering,doorWalking,
                    grounded,velocity.x,velocity.y);
                string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(heroAnimator,"CurrentClip"),"name"));
                bool playing=CapAccess.Flag(heroAnimator,"Playing");
                bool hard=Convert.ToString(CapAccess.Read(hero,"hero_state"))=="hard_landing"||(playing&&clip=="HardLand");
                if(entering&&grounded&&transitionFell&&transitionLandingAge<0f)
                {
                    transitionLandingAge=0f;transitionHardLanding=hard||CapAccess.Flag(state,"willHardLand");
                    log?.LogInfo("Mario's room-entry fall reached the floor; landing presentation started before control returns.");
                }
                animation=TransitionLanding(animation,grounded,hard,playing&&clip=="Land");
                animation=EntryLanding(animation,entering,grounded,transitionFell,transitionHardLanding,
                    transitionLandingAge,transitionHardLanding?Mathf.Max(.2f,CapAccess.Number(hero,"HARD_LANDING_TIME")):.2f);
                animation=TransitionAction(animation,hurt,CapAccess.Flag(state,"superDashing")||CapAccess.Flag(hero,"exitedSuperDashing"));
                heldFrame=frozen?0:-1;
                return hurt?TransitionHurtFacing(velocity.x,right):TransitionFacing(transitionGate,entering,velocity.x,right);
            }
            if(tailClip!=null)return ClosingFacing(tailYaw,CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight"),ClosingProgress());
            if(background)return Mathf.PI;
            if(scriptedFacing)return CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight")?Mathf.PI*.25f:-Mathf.PI*.25f;
            if(animation==Walk)
            {
                var body=hero.GetComponent<Rigidbody2D>();
                if(body!=null&&Mathf.Abs(body.velocity.x)>.01f)return body.velocity.x>0?Mathf.PI*.5f:-Mathf.PI*.5f;
            }
            return Facing(speaker.transform.position.x,hero.transform.position.x,
                CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight"));
        }
        internal static void Draw(object controller)
        {
            ObserveTransition(controller);
            ObserveHeadReturn(controller);
            ObserveNativeControl(controller);
            if(!requested)return;
            if(!Valid(controller)){Stop();return;}
            long timing=PresentationTiming.Begin();
            bool starting=pose==null;
            try
            {
                if(returning)
                {
                    if(modelObject!=null)Place(renderedFeet);
                    if(knight!=null)knight.forceRenderingOff=true;
                    return;
                }
                bool first=pose==null;
                if(actionPose&&actionGroup=="focus"&&animation==SpellMario.Crawl)
                {
                    // Unn moves through iTween, so Rigidbody velocity alone can be zero.
                    float x=speaker.transform.position.x;
                    crawlSpeed=SpellMario.CrawlSpeed(crawlX,x,Time.deltaTime);crawlX=x;
                }
                if(first)
                {
                    if(Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)deactivate.Invoke(controller,null);
                    hero=CapAccess.Read(heroType,"instance") as Component;
                    knight=hero.GetComponent<MeshRenderer>();
                    if(knight==null){Stop();return;}
                    heroAnimator=hero.GetComponent(animatorType);
                    if(renderer==null)oldHidden=(bool)AccessTools.Method(controller.GetType(),"NativeBodyWasHidden").Invoke(controller,new object[]{hero});
                    string library=Path.Combine(Path.GetDirectoryName(nativeType.Assembly.Location),"sm64.dll");
                    // Start grounded on the existing private stage; immediately
                    // replace the sleep action before it is ever simulated.
                    pose=new OpeningMarioPose(nativeType,library,0f,true);
                    pose.TalkFacing(PoseFacing(),animation==0x87);
                    BenchRestAudio.Install(library);
                    if(renderer==null)renderer=Activator.CreateInstance(rendererType,new object[]{"Hollow Mario conversation"});
                    model=CapAccess.Read(renderer,"MeshRenderer") as MeshRenderer;
                    modelObject=CapAccess.Read(renderer,"Object") as GameObject;
                    accumulator=1f/30f;
                }
                if(DeathMario.Active){DeathMario.Step(Time.deltaTime);animation=DeathMario.Animation;}
                if(DeathCap.Pending)accumulator=Mathf.Max(accumulator,1f/30f);
                if(actionPose&&actionGroup=="dream"&&animation==DreamTwirl)twirlAge+=Time.deltaTime;
                if(transition&&transitionLandingAge>=0f)transitionLandingAge+=Time.deltaTime;
                PoseFacing(); // Resolve transition walking/falling before choosing animation pace.
                ArrivalMario.Sample arrival=default(ArrivalMario.Sample);
                bool timedArrival=actionPose&&(actionGroup=="wake"||actionGroup=="hazard-return")&&ArrivalMario.TrySample(out arrival);
                if(transition&&animation==ArrivalMario.HardLanding&&transitionLandingAge>=0f)
                {
                    timedArrival=true;
                    arrival=new ArrivalMario.Sample{Animation=ArrivalMario.HardLanding,
                        Frame=ArrivalMario.HardLandingFrame(transitionLandingAge,Mathf.Max(.2f,CapAccess.Number(hero,"HARD_LANDING_TIME")))};
                }
                if(actionPose&&actionGroup=="upgrade")timedArrival=UpgradeKnockoutMario.TrySample(out arrival);
                if(actionPose&&actionGroup==MonarchWingsMario.Group)timedArrival=MonarchWingsMario.TrySample(out arrival);
                if(actionPose&&actionGroup=="web")timedArrival=WebbedMario.TrySample(out arrival);
                if(actionPose&&actionGroup=="dream-arrival")timedArrival=DreamArrivalMario.TrySample(out arrival);
                if(actionPose&&actionGroup==DreamerSequenceMario.Group)timedArrival=DreamerSequenceMario.TrySample(out arrival);
                if(actionPose&&actionGroup==BeastBenchMario.Group)timedArrival=BeastBenchMario.TrySample(out arrival);
                if(actionPose&&actionGroup==BirthplaceMario.Group&&BirthplaceMario.TrySample(out ArrivalMario.Sample birth,out float birthYaw))
                {timedArrival=true;arrival=birth;actionYaw=birthYaw;}
                DreamNailMario.Sample dream;
                if(actionPose&&actionGroup=="dream"&&DreamNailMario.TrySample(out dream))
                {
                    timedArrival=true;
                    arrival=new ArrivalMario.Sample{Animation=dream.Animation,Frame=dream.Frame};
                    actionYaw=dream.Yaw;
                }
                if(timedArrival)animation=arrival.Animation;
                bool blendPose=timedArrival&&(actionGroup=="web"||actionGroup==BeastBenchMario.Group);
                var body=hero.GetComponent<Rigidbody2D>();
                accumulator+=Time.deltaTime*WalkRate(animation,body!=null?body.velocity.x:0f);
                if(blendPose)accumulator=1f/30f; // Re-sample fractional keyframes at display rate.
                for(int count=0;accumulator>=1f/30f&&count<4;count++)
                {
                    accumulator-=1f/30f;
                    pose.TalkFacing(PoseFacing(),animation==0x87);
                    pose.LimitAnimationFrame(animation,actionPose&&actionGroup=="focus"&&animation==SpellMario.Crawl
                        ?SpellMario.CrawlHold(crawlSpeed):heldFrame);
                    int previous=MapFallFlourish.PushCinematicAnimation(animation);
                    bool quiet=BenchRestAudio.Begin();
                    try
                    {
                        if(timedArrival&&arrival.Frame>=0)
                        {
                            // Selecting a different SM64 animation resets its frame.
                            // Prime that selection before scrubbing to the host's time.
                            if(Convert.ToInt32(CapAccess.Read(pose.State,"animID"))!=animation)
                                buffers.SetValue(renderer,pose.Tick(buffers.GetValue(renderer),true));
                            pose.SetAnimationFrame((short)(arrival.Frame-1));
                        }
                        buffers.SetValue(renderer,pose.Tick(buffers.GetValue(renderer),true));
                    }
                    finally{BenchRestAudio.End(quiet);MapFallFlourish.PopCinematicAnimation(previous);}
                    takeTick.Invoke(renderer,null);
                    if(blendPose)
                    {
                        // Renderer already interpolates its previous/current geometry.
                        // Supply adjacent ROM frames rather than holding each for .25 s.
                        int key=MapFallFlourish.PushCinematicAnimation(animation);
                        bool silent=BenchRestAudio.Begin();
                        try
                        {
                            pose.SetAnimationFrame((short)((arrival.Blend>0f?arrival.NextFrame:arrival.Frame)-1));
                            buffers.SetValue(renderer,pose.Tick(buffers.GetValue(renderer),true));
                        }
                        finally{BenchRestAudio.End(silent);MapFallFlourish.PopCinematicAnimation(key);}
                        takeTick.Invoke(renderer,null);first=false;
                    }
                    if(first){takeTick.Invoke(renderer,null);first=false;}
                }
                accumulator=Mathf.Min(accumulator,1f/30f);
                if(actionPose&&actionGroup=="death"&&DeathCap.Pending)DeathCap.Detach(pose,renderer,animation);
                if(hazardPose)hazardAge+=Time.deltaTime;
                Vector2 origin=(Vector2)CapAccess.Read(mathType,"Origin");
                Vector3 nativeFeet=new Vector3(origin.x+CapAccess.Number(pose.State,"px")*.0105f,
                    origin.y+CapAccess.Number(pose.State,"py")*.0105f,
                    CapAccess.Number(mathType,"LaneZ")-CapAccess.Number(pose.State,"pz")*.0105f);
                render.Invoke(renderer,new object[]{blendPose?arrival.Blend:Mathf.Clamp01(accumulator*30f),nativeFeet});
                renderedFeet=nativeFeet;Place(nativeFeet);
                if(actionPose&&actionGroup=="death")DeathCap.Place(modelObject,nativeFeet);
                model.sortingLayerID=knight.sortingLayerID;model.sortingOrder=knight.sortingOrder;
                if(actionPose&&actionGroup=="web")WebbedMario.Cover(model);
                if(actionPose&&actionGroup==ShadePoolMario.Group)ShadePoolMario.Cover(model,nativeFeet);
                if(actionPose&&actionGroup==BirthplaceMario.Group)BirthplaceMario.Cover(model,nativeFeet,Convert.ToInt32(CapAccess.Read(renderer,"Triangles")));
                if(actionPose)
                {
                    if(poseColors==null)poseColors=new MaterialPropertyBlock();
                    Texture dark=darkPose?MarioAppearance.ShadeTexture?.Invoke():null;
                    Color tint=darkPose&&dark==null?MarioAppearance.ShadeTint:Color.white;
                    tint.a=hazardPose?Mathf.Min(HazardFade(hazardAge),SpellMario.HazardEffectAlpha):1f;
                    poseColors.SetTexture("_MainTex",dark??model.sharedMaterial.mainTexture);
                    poseColors.SetColor("_Color",tint);model.SetPropertyBlock(poseColors);
                }
                if(transition)
                {
                    // The native sprite can remain disabled during airborne entry.
                    // Preserve its intentional pre-entry wait, then let this view own
                    // visibility instead of inheriting a stale hidden Knight sprite.
                    string phase=Convert.ToString(CapAccess.Read(hero,"transitionState"));
                    model.enabled=TransitionVisible(entering,phase=="WAITING_TO_ENTER_LEVEL",knight.enabled);
                    int visibility=(model.enabled?1:0)|(knight.enabled?2:0);
                    if(visibility!=transitionVisibility)
                    {
                        transitionVisibility=visibility;
                        log?.LogInfo("Mario transition view: gate="+transitionGate+" entry="+entering+" phase="+phase
                            +" visible="+model.enabled+" native sprite="+knight.enabled+" previous hidden="+oldHidden
                            +" animation="+animation.ToString("X")+" triangles="+CapAccess.Read(renderer,"Triangles"));
                    }
                }
                else model.enabled=actionPose&&actionGroup==BirthplaceMario.Group
                    ?BirthplaceMario.Active:actionPose&&actionGroup=="web"?WebbedMario.OwnsSequence:
                    actionPose&&actionGroup==BeastBenchMario.Group?BeastBenchMario.Active:(actionPose||knight.enabled)&&!oldHidden;
                knight.forceRenderingOff=true;
                DeathMario.HideNativeBody();
            }
            catch(Exception error){Stop();Warn(error);}
            finally{PresentationTiming.End(timing,starting?"pose setup":"pose draw");}
        }
        private static void Place(Vector3 nativeFeet)
        {
            var box=hero.GetComponent<BoxCollider2D>();
            Vector3 anchor=new Vector3(hero.transform.position.x,box!=null?box.bounds.min.y:hero.transform.position.y,hero.transform.position.z);
            if(hazardPose)anchor=hazardAnchor+Vector3.up*(.7f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(hazardAge/.45f)));
            if(DeathMario.Active)anchor=DeathMario.Feet;
            if(actionGroup=="upgrade"&&UpgradeKnockoutMario.Active)anchor=UpgradeKnockoutMario.Anchor;
            if(actionGroup==MonarchWingsMario.Group&&MonarchWingsMario.Active)anchor=MonarchWingsMario.Anchor;
            if(actionGroup=="web"&&WebbedMario.Active)anchor=WebbedMario.Anchor;
            if(actionGroup=="dream-arrival"&&DreamArrivalMario.Active)anchor=DreamArrivalMario.Anchor;
            if(actionGroup==DreamerSequenceMario.Group&&DreamerSequenceMario.Active)anchor=DreamerSequenceMario.Anchor;
            if(actionGroup==BeastBenchMario.Group&&BeastBenchMario.Active)anchor=BeastBenchMario.Anchor;
            if(actionGroup==ShadePoolMario.Group&&ShadePoolMario.Active)anchor=ShadePoolMario.Anchor;
            if(actionGroup==BirthplaceMario.Group&&BirthplaceMario.Active)anchor=BirthplaceMario.Anchor;
            modelObject.transform.position=anchor-nativeFeet;
        }
        private static void Warn(Exception error)
        {if(warnings++<3)log?.LogWarning("Mario's conversation pose unavailable: "+error.GetBaseException().Message);}
        internal static void Stop()
        {
            if(WebbedMario.OwnsSequence)WebbedMario.Trace("cover stopped; group="+actionGroup+"; returning="+returning);
            requested=returning=background=nativeCover=transition=entering=doorWalking=actionPose=hazardPose=darkPose=actionHeld=challengePose=false;
            scriptedFacing=false;
            actionGroup=null;
            ArrivalMario.Forget();
            UpgradeKnockoutMario.Forget();
            MonarchWingsMario.Forget();
            DreamNailMario.Forget();
            WebbedMario.Forget();
            DreamArrivalMario.Forget();
            DreamerSequenceMario.Forget();
            BeastBenchMario.Forget();
            ShadePoolMario.Forget();
            BirthplaceMario.Forget();
            tailClip=null;tailAnimator=heroAnimator=null;tailYaw=0f;
            transitionGate=5;transitionVisibility=-1;animation=Idle;heldFrame=-1;hazardAge=twirlAge=0f;
            transitionFell=transitionHardLanding=false;transitionLandingAge=-1f;
            SpellMario.RestoreHazardEffects();
            DeathMario.Stop();
            try{pose?.Dispose();}
            finally
            {
                pose=null;
                try{if(model!=null)model.enabled=false;if(renderer!=null)dispose.Invoke(renderer,null);}
                finally
                {
                    renderer=null;if(knight!=null)knight.forceRenderingOff=oldHidden;
                    speaker=null;hero=null;knight=model=null;modelObject=null;accumulator=0f;
                }
            }
        }
    }
}
