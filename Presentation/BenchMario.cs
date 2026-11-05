using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Bench Control remains the sole owner of interaction, physics, inventory,
    // healing, saving and exit input. Only the visible body is substituted.
    internal static class BenchMario
    {
        private static Type controllerType,nativeType,rendererType,mathType,heroType;
        private static MethodInfo deactivate,takeTick,render,dispose,setFace;
        private static FieldInfo buffers;
        private static ManualLogSource log;
        private static GameObject bench;
        private static Component hero;
        private static MeshRenderer knight,model;
        private static GameObject modelObject;
        private static object renderer;
        private static OpeningMarioPose pose;
        private static bool requested,leaving,released,oldHidden,wakeStarted,readingMap;
        private static float accumulator;
        private static int animation=SitDown,previousFrame=-1,animationTicks,warnings;
        private static object returnController;
        private static GameObject returnObject;
        private static Vector3 lastAnchor,returnAnchor,returnOffset;
        private static Quaternion lastTilt=Quaternion.identity,returnTilt=Quaternion.identity;
        private static float returnAge;
        private static bool returnFacing,returnStarted;
        private static bool exitGroundFound,returnSettled;
        private static Vector3 exitSeat,exitFloor;
        private static Quaternion exitTilt;
        private static float exitAge,exitDuration=.25f;
        private static readonly RaycastHit2D[] floorHits=new RaycastHit2D[16];
        internal const int SitDown=0x84,Sleep=0x85,Wake=0xC8;
        internal const int SeatedMap=0x45; // Butt slide holding an item; pose only, no sliding physics.
        internal const int WakeLastFrame=19;
        internal const float SeatLift=.38f;
        internal const float ExitBlendSeconds=.22f;
        internal static bool Presenting=>pose!=null;
        internal static Vector3 SeatOffset(Vector2 colliderOffset,Vector2 colliderSize)
            =>new Vector3(colliderOffset.x,colliderOffset.y-colliderSize.y*.5f+SeatLift,0f);

        internal static void Arm(Harmony harmony,Type controller,Type native,ManualLogSource logger)
        {
            controllerType=controller;nativeType=native;log=logger;
            heroType=AccessTools.TypeByName("HeroController");
            rendererType=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            mathType=controller.Assembly.GetType("MarioHollow.MarioMath",true);
            deactivate=AccessTools.Method(controller,"Deactivate");
            setFace=AccessTools.Method(native,"sm64_set_mario_faceangle");
            buffers=AccessTools.Field(rendererType,"Buffers");
            takeTick=AccessTools.Method(rendererType,"TakeTick");
            render=AccessTools.Method(rendererType,"Render");dispose=AccessTools.Method(rendererType,"Dispose");
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(BenchMario),nameof(BeforeState)));
            harmony.Patch(AccessTools.Method(controller,"Update"),
                prefix:new HarmonyMethod(typeof(BenchMario),nameof(BeforeUpdate)));
            harmony.Patch(AccessTools.Method(controller,"Tick"),
                prefix:new HarmonyMethod(typeof(BenchMario),nameof(BeforePlayableTick)){priority=Priority.First+100});
            harmony.Patch(render,postfix:new HarmonyMethod(typeof(BenchMario),nameof(AfterPlayableRender)));
        }
        internal static bool StartsRest(string state)
            =>state=="Start Rest"||state=="Init Resting"||state=="Init Resting 2";
        internal static bool StartsLeaving(string state)
            // Get Up/Left/Right are requests. The native inventory/pause check
            // can reject them and return to Resting without actually leaving.
            =>state=="Wake?"||state=="Get Off";
        internal static bool EndsRest(string state)
            =>state=="Regain Control"||state=="Idle"||state=="Idle Start Pause"||state=="Inert";
        internal static int MapReadingState(string state)
        {
            switch(state)
            {
                case "Open Map":case "Map Idle":return 1;
                case "Close Map":case "Close Anim":case "Inv Map Open":case "Resting":case "Sit Idle":
                case "Fall Asleep":case "Wake?":case "Get Off":return 0;
                default:return -1;
            }
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Bench Control"
                ||!(CapAccess.Read(fsm,"GameObject") is GameObject target))return;
            string state=Convert.ToString(CapAccess.Read(__instance,"Name"));
            if(StartsRest(state))
            {
                object controller=CapAccess.Read(controllerType,"Instance");
                if(!CapAccess.Flag(controller,"Wanted")||OpeningMario.Presenting)return;
                if(!requested||bench!=target||released||state=="Start Rest")
                {
                    Stop();bench=target;requested=true;
                    // A respawn already starts seated; a normal interaction sits down.
                    animation=state=="Start Rest"?SitDown:Sleep;
                }
                return;
            }
            if(!requested||target!=bench)return;
            int map=MapReadingState(state);
            if(!leaving&&map>=0&&readingMap!=(map==1))
            {
                readingMap=map==1;animation=readingMap?SeatedMap:Sleep;
                previousFrame=-1;animationTicks=0;
                pose?.TalkFacing(0f,!readingMap); // Awake while reading; quiet seated sleep afterwards.
                pose?.SetAnimationFrame(-1);
            }
            if(StartsLeaving(state)&&!leaving){leaving=true;animation=Wake;previousFrame=-1;animationTicks=0;}
            if(StartsLeaving(state)&&!wakeStarted)exitDuration=WakeDuration(__instance);
            if(EndsRest(state))released=true;
        }
        private static float WakeDuration(object state)
        {
            Component current=CapAccess.Read(heroType,"instance") as Component;
            object control=CapAccess.Read(current,"animCtrl");
            if(CapAccess.Read(state,"Actions") is Array actions)
                foreach(object action in actions)
                {
                    if(action?.GetType().Name!="Tk2dPlayAnimationWithEvents")continue;
                    string clip=Convert.ToString(CapAccess.Read(CapAccess.Read(action,"clipName"),"Value"));
                    if(control!=null&&!string.IsNullOrEmpty(clip))
                    {
                        float seconds=Convert.ToSingle(CapAccess.Call(control,"GetClipDuration",clip));
                        if(seconds>0f)return Mathf.Max(.15f,seconds);
                    }
                }
            return .25f;
        }
        private static bool FindExitFloor(Vector3 start,out Vector3 floor)
        {
            floor=start;
            var filter=new ContactFilter2D{useTriggers=false};filter.SetLayerMask(LayerMask.GetMask("Terrain","Soft Terrain"));
            int count=Physics2D.Raycast((Vector2)start+Vector2.up*.1f,Vector2.down,filter,floorHits,6f);
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=floorHits[i];
                if(hit.collider==null||hit.normal.y<.5f||hit.collider.transform.IsChildOf(hero.transform)
                    ||(bench!=null&&hit.collider.transform.IsChildOf(bench.transform))||hit.distance>=nearest)continue;
                nearest=hit.distance;floor=new Vector3(start.x,hit.point.y+.01f,start.z);
            }
            return !float.IsPositiveInfinity(nearest);
        }
        internal static float WakeProgress(float seconds,float duration)
            =>Mathf.SmoothStep(0f,1f,seconds/Mathf.Max(.01f,duration));
        private static bool BeforeUpdate(object __instance)
        {
            if(!requested)return true;
            if(!Valid(__instance)){Stop();return true;}
            if(released&&CanResume())
            {
                // Restore then let ordinary activation hide the Knight again in
                // this same Update, before anything is drawn. No one-frame reveal.
                bool wasVisible=pose!=null&&modelObject!=null;
                Vector3 anchor=lastAnchor;Quaternion tilt=lastTilt;
                bool settled=false;
                if(wasVisible&&exitGroundFound&&FindExitFloor(new Vector3(hero.transform.position.x,exitSeat.y,exitSeat.z),out Vector3 floor))
                {
                    // Bench Control has already finished and returned input. Put
                    // its body on that same verified floor before normal Mario
                    // activation, avoiding a second spawn/drop after standing up.
                    var box=hero.GetComponent<BoxCollider2D>();var body=hero.GetComponent<Rigidbody2D>();
                    if(box!=null&&Mathf.Abs(floor.y-box.bounds.min.y)<3f)
                    {
                        hero.transform.position+=Vector3.up*(floor.y-box.bounds.min.y);
                        if(body!=null){body.position=hero.transform.position;body.velocity=Vector2.zero;}
                        Physics2D.SyncTransforms();settled=true;
                    }
                }
                Stop();
                if(wasVisible){returnController=__instance;returnAnchor=anchor;returnTilt=tilt;returnFacing=true;returnSettled=settled;}
                return true;
            }
            // The ordinary controller must not reclaim a body while inventory or
            // a bench animation still owns it. Its activation path runs on exit.
            return false;
        }
        private static bool Valid(object controller)
        {
            if(bench==null||!bench.scene.isLoaded||!bench.activeInHierarchy
                ||!CapAccess.Flag(controller,"Wanted")||OpeningMario.Presenting)return false;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            return current!=null&&!CapAccess.Flag(CapAccess.Read(current,"cState"),"dead")
                &&!CapAccess.Flag(CapAccess.Read(current,"cState"),"hazardDeath");
        }
        private static bool CanResume()
        {
            Component current=CapAccess.Read(heroType,"instance") as Component;
            return current!=null&&!CapAccess.Flag(current,"controlReqlinquished")&&CapAccess.Flag(current,"acceptingInput");
        }
        internal static void Draw(object controller)
        {
            if(returnController!=null&&(!ReferenceEquals(controller,returnController)
                ||!CapAccess.Flag(controller,"Wanted")
                ||(returnStarted&&CapAccess.Read(controller,"Renderer")==null)))ClearReturn();
            if(!requested)return;
            if(!Valid(controller)){Stop();return;}
            try
            {
                bool first=pose==null;
                if(pose==null)
                {
                    // Finish the normal handover before borrowing the parked actor.
                    if(Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)deactivate.Invoke(controller,null);
                    hero=CapAccess.Read(heroType,"instance") as Component;
                    knight=hero.GetComponent<MeshRenderer>();
                    if(knight==null){Stop();return;}
                    oldHidden=(bool)AccessTools.Method(controller.GetType(),"NativeBodyWasHidden").Invoke(controller,new object[]{hero});
                    string library=Path.Combine(Path.GetDirectoryName(nativeType.Assembly.Location),"sm64.dll");
                    pose=new OpeningMarioPose(nativeType,library,0f,true); // Face the camera.
                    if(readingMap)pose.TalkFacing(0f);
                    BenchRestAudio.Install(library);
                    renderer=Activator.CreateInstance(rendererType,new object[]{"Hollow Mario bench"});
                    model=CapAccess.Read(renderer,"MeshRenderer") as MeshRenderer;
                    modelObject=CapAccess.Read(renderer,"Object") as GameObject;
                    accumulator=1f/30f;
                }
                if(leaving&&!wakeStarted)
                {
                    exitSeat=lastAnchor;exitTilt=lastTilt;exitAge=0f;
                    if(first)
                    {
                        var seatBox=hero.GetComponent<BoxCollider2D>();
                        exitSeat=hero.transform.TransformPoint(seatBox!=null?SeatOffset(seatBox.offset,seatBox.size):Vector3.up*SeatLift);
                        exitTilt=Quaternion.Euler(0f,0f,hero.transform.eulerAngles.z);
                    }
                    exitGroundFound=FindExitFloor(exitSeat,out exitFloor);
                    // Waiting-for-dialogue supplies a stationary actor without
                    // SM64's waking action skipping the beginning of this clip.
                    pose.TalkFacing(0f);pose.SetAnimationFrame(-1);wakeStarted=true;
                }
                if(leaving)exitAge+=Time.deltaTime;
                float wakeProgress=WakeProgress(exitAge,exitDuration);
                accumulator+=Time.deltaTime;
                for(int count=0;accumulator>=1f/30f&&count<4;count++)
                {
                    accumulator-=1f/30f;
                    if(leaving)pose.SetAnimationFrame((short)(Mathf.RoundToInt(wakeProgress*WakeLastFrame)-1));
                    int previous=MapFallFlourish.PushCinematicAnimation(animation);
                    bool previousQuiet=BenchRestAudio.Begin();
                    try{buffers.SetValue(renderer,pose.Tick(buffers.GetValue(renderer),true));}
                    finally{BenchRestAudio.End(previousQuiet);MapFallFlourish.PopCinematicAnimation(previous);}
                    takeTick.Invoke(renderer,null);
                    if(first){takeTick.Invoke(renderer,null);first=false;}
                    int frame=Convert.ToInt32(CapAccess.Read(pose.State,"animFrame"));
                    NoteFrame(frame);
                }
                accumulator=Mathf.Min(accumulator,1f/30f);
                Vector2 origin=(Vector2)CapAccess.Read(mathType,"Origin");
                Vector3 nativeFeet=new Vector3(origin.x+CapAccess.Number(pose.State,"px")*.0105f,
                    origin.y+CapAccess.Number(pose.State,"py")*.0105f,
                    CapAccess.Number(mathType,"LaneZ")-CapAccess.Number(pose.State,"pz")*.0105f);
                render.Invoke(renderer,new object[]{Mathf.Clamp01(accumulator*30f),nativeFeet});
                // Native Bench Control owns the seat position and RestBenchTilt
                // rotation/translation. Use the body's local bottom, not the
                // rotated world AABB, which shifts downward on a tilted bench.
                var box=hero.GetComponent<BoxCollider2D>();
                Vector3 offset=box!=null?SeatOffset(box.offset,box.size):new Vector3(0f,SeatLift,0f);
                Vector3 anchor=hero.transform.TransformPoint(offset);
                Quaternion tilt=Quaternion.Euler(0f,0f,hero.transform.eulerAngles.z);
                if(leaving&&exitGroundFound)
                {
                    anchor=Vector3.Lerp(exitSeat,exitFloor,wakeProgress);
                    tilt=Quaternion.Slerp(exitTilt,Quaternion.identity,wakeProgress);
                }
                lastAnchor=anchor;lastTilt=tilt;
                // Geometry contains stage-world coordinates: rotate around the
                // actor's feet, rather than rotating the remote stage origin.
                modelObject.transform.SetPositionAndRotation(anchor-tilt*nativeFeet,tilt);
                model.sortingLayerID=knight.sortingLayerID;model.sortingOrder=knight.sortingOrder;
                model.enabled=knight.enabled;knight.forceRenderingOff=true;
            }
            catch(Exception error)
            {
                Stop();if(warnings++<3)log?.LogWarning("Mario's bench pose unavailable: "+error.GetBaseException().Message);
            }
        }
        private static void BeforePlayableTick(object __instance)
        {
            if(!returnFacing||!ReferenceEquals(returnController,__instance))return;
            int id=Convert.ToInt32(CapAccess.Read(__instance,"MarioId"));
            if(id<0)return;
            // Activate normally chooses left/right. Override only this bench
            // handover, before its first simulation/geometry tick. Normal input
            // is free to turn him from here; there is no persistent yaw lock.
            setFace.Invoke(null,new object[]{id,0f});
            foreach(string field in new[]{"State","PrevState"})
            {
                object state=CapAccess.Read(__instance,field);
                if(state!=null){CapAccess.Write(state,"faceAngle",0f);CapAccess.Write(__instance,field,state);}
            }
            returnFacing=false;
        }
        internal static float ExitProgress(float seconds)
            =>Mathf.SmoothStep(0f,1f,seconds/ExitBlendSeconds);
        internal static Vector3 ExitTranslation(Vector3 offset,Vector3 feet,Quaternion tilt,float progress)
            =>offset*(1f-progress)+feet-tilt*feet;
        private static void AfterPlayableRender(object __instance,Vector3 __1)
        {
            if(returnController==null)return;
            if(!ReferenceEquals(__instance,CapAccess.Read(returnController,"Renderer")))return;
            if(returnSettled){ClearReturn();return;}
            try
            {
                if(!returnStarted)
                {
                    returnObject=CapAccess.Read(__instance,"Object") as GameObject;
                    if(returnObject==null){ClearReturn();return;}
                    // Activate creates/ticks the renderer, but returns before
                    // drawing it. Its new mesh bounds are still zero then.
                    // Start only after a real Render, using that call's feet.
                    returnOffset=returnAnchor-__1;
                    returnStarted=true;returnAge=0f;
                }
                float progress=ExitProgress(returnAge);
                Quaternion tilt=Quaternion.Slerp(returnTilt,Quaternion.identity,progress);
                returnObject.transform.SetPositionAndRotation(ExitTranslation(returnOffset,__1,tilt,progress),tilt);
                returnAge+=Time.deltaTime;
                if(progress>=1f)ClearReturn();
            }
            catch(Exception error)
            {ClearReturn();if(warnings++<3)log?.LogWarning("Mario's bench exit blend unavailable: "+error.GetBaseException().Message);}
        }
        private static void ClearReturn()
        {
            if(returnObject!=null)returnObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            returnController=null;returnObject=null;returnFacing=returnStarted=returnSettled=false;returnAge=0f;
        }
        private static void NoteFrame(int frame)
        {
            // End on the real animation's final frame, independent of ROM
            // animation length. The sleep loop never advances into lying down.
            if(animation==SitDown&&animationTicks>2&&frame<=previousFrame)
            {animation=Sleep;animationTicks=0;previousFrame=-1;}
            else{previousFrame=frame;animationTicks++;}
        }
        internal static void SceneLoaded(string name)
        {ClearReturn();if(requested&&(bench==null||name=="Menu_Title"||name=="Quit_To_Menu"))Stop();}
        internal static void Stop()
        {
            ClearReturn();
            requested=false;leaving=false;released=false;wakeStarted=false;readingMap=false;
            exitGroundFound=false;exitAge=0f;exitDuration=.25f;
            try{pose?.Dispose();}
            finally
            {
                pose=null;
                try{if(renderer!=null)dispose.Invoke(renderer,null);}
                finally
                {
                    renderer=null;if(knight!=null)knight.forceRenderingOff=oldHidden;
                    bench=null;hero=null;knight=model=null;modelObject=null;
                    accumulator=0;animation=SitDown;previousFrame=-1;animationTicks=0;
                }
            }
        }
    }
}
