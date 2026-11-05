using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnitySceneManager=UnityEngine.SceneManagement.SceneManager;

namespace MarioHollow
{
    // Observers only: no ref camera arguments/results, bool prefixes, physics
    // queries during regular frames, or writes to game/camera/player objects.
    internal static class CameraTrace
    {
        private static Func<bool> enabled;
        private static Func<Collider2D,bool> probe;
        private static readonly FieldInfo currentArea=AccessTools.Field(typeof(CameraController),"currentLockArea");
        private static readonly FieldInfo cameraHero=AccessTools.Field(typeof(CameraController),"hero_ctrl");
        private static readonly HashSet<long> stays=new HashSet<long>();
        private static StreamWriter writer;
        private static string path,version;
        private static bool failed,haveSample;
        private static string lastError;
        private static float nextSample,lastCameraY,lastHeroY,nextNearby;
        private static int scene=-1,readFrame=-1,targetFrame=-1;
        private static Vector3 readPosition;
        private static Vector2 targetVelocity;
        private static bool targetFalling,targetGrounded;
        [ThreadStatic]private static Collider2D contact;
        [ThreadStatic]private static CameraLockArea contactArea;
        [ThreadStatic]private static string contactPhase;
        internal struct Scope {internal Collider2D Contact;internal CameraLockArea Area;internal string Phase;}
        internal struct LockState {internal int Area,Count,Mode;internal float Min,Max;internal bool Valid;}
        private static bool Active=>!failed&&enabled!=null&&enabled();
        [Conditional("HOLLOW_MARIO_DIAGNOSTICS")]
        internal static void Install(Harmony hooks,Assembly companion,Func<Collider2D,bool> sensor)
        {
            enabled=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),AccessTools.PropertyGetter(companion.GetType("HollowMario.DebugSettings",true),"CameraTrace"));
            probe=sensor;version=Convert.ToString(companion.GetType("HollowMario.Plugin",true).GetField("PluginVersion",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue());
            path=Path.Combine(Path.GetDirectoryName(typeof(CameraTrace).Assembly.Location),"diagnostics","camera-trace.log");
            foreach(string phase in new[]{"Enter","Stay","Exit"})
                hooks.Patch(AccessTools.Method(typeof(CameraLockArea),"OnTrigger"+phase+"2D"),
                    prefix:new HarmonyMethod(typeof(CameraTrace),nameof(BeforeContact)){priority=Priority.First+1000},
                    finalizer:new HarmonyMethod(typeof(CameraTrace),nameof(AfterContact)));
            foreach(string name in new[]{"LockToArea","ReleaseLock"})
                hooks.Patch(AccessTools.Method(typeof(CameraController),name),
                    prefix:new HarmonyMethod(typeof(CameraTrace),nameof(BeforeLock)){priority=Priority.First+1000},
                    postfix:new HarmonyMethod(typeof(CameraTrace),nameof(AfterLock)){priority=Priority.Last});
            hooks.Patch(AccessTools.Method(typeof(CameraController),"SetMode"),prefix:new HarmonyMethod(typeof(CameraTrace),nameof(Mode)));
            hooks.Patch(AccessTools.Method(typeof(CameraController),"LateUpdate"),postfix:new HarmonyMethod(typeof(CameraTrace),nameof(Frame)){priority=Priority.Last});
            hooks.Patch(AccessTools.Method(typeof(CameraTarget),"Update"),prefix:new HarmonyMethod(typeof(CameraTrace),nameof(TargetInput)){priority=Priority.Last});
            hooks.Patch(AccessTools.Method(companion.GetType("HollowMario.MarioView",true),"ReadHeroPosition"),postfix:new HarmonyMethod(typeof(CameraTrace),nameof(PositionRead)));
        }
        private static void BeforeContact(CameraLockArea __instance,Collider2D __0,MethodBase __originalMethod,out Scope __state)
        {
            __state=new Scope{Contact=contact,Area=contactArea,Phase=contactPhase};
            if(!Active)return;
            contact=__0;contactArea=__instance;contactPhase=__originalMethod.Name;
            try
            {
                EnsureScene();long pair=((long)__instance.GetInstanceID()<<32)^(uint)(__0!=null?__0.GetInstanceID():0);
                if(__0==null||(!(probe!=null&&probe(__0))&&!__0.CompareTag("Player")&&!__0.CompareTag("HeroBox")))return;
                bool stay=contactPhase.Contains("Stay");
                if(!stay||stays.Add(pair))Write("CONTACT "+contactPhase+" zone="+Zone(__instance)+" collider="+Shape(__0));
            }catch(Exception error){Fail(error);}
        }
        private static void AfterContact(Scope __state)
        {contact=__state.Contact;contactArea=__state.Area;contactPhase=__state.Phase;}
        private static LockState ReadLock(CameraController camera)
            =>new LockState{Valid=true,Area=Id(currentArea.GetValue(camera) as UnityEngine.Object),Count=camera.lockZoneList?.Count??0,
                Mode=(int)camera.mode,Min=camera.yLockMin,Max=camera.yLockMax};
        private static void BeforeLock(CameraController __instance,out LockState __state)
        {
            __state=default(LockState);if(!Active)return;
            try{__state=ReadLock(__instance);}catch(Exception error){Fail(error);}
        }
        private static void AfterLock(CameraController __instance,CameraLockArea __0,MethodBase __originalMethod,LockState __state)
        {
            if(!Active||!__state.Valid)return;
            try
            {
                LockState after=ReadLock(__instance);
                if(after.Area==__state.Area&&after.Count==__state.Count&&after.Mode==__state.Mode&&after.Min==__state.Min&&after.Max==__state.Max)return;
                Write("LOCK_CHANGE "+__originalMethod.Name+" requested="+Zone(__0)+" old="+__state.Area+" new="+after.Area
                    +" count="+__state.Count+"->"+after.Count+" y="+F(after.Min)+".."+F(after.Max)
                    +" sourcePhase="+(contactPhase??"direct call")+" sourceZone="+Id(contactArea)+" sourceCollider="+Shape(contact)
                    +(contact==null?" caller="+Caller():""));
                Snapshot(__instance,"LOCK_STATE",true);
            }catch(Exception error){Fail(error);}
        }
        private static void Mode(CameraController __instance,object __0)
        {
            if(!Active||Convert.ToInt32(__0)==(int)__instance.mode)return;
            try{Write("MODE "+__instance.mode+"->"+__0+" caller="+Caller());}catch(Exception error){Fail(error);}
        }
        private static void TargetInput(CameraTarget __instance)
        {
            if(!Active)return;
            try{var hero=__instance.hero_ctrl;if(hero==null)return;targetFrame=Time.frameCount;targetVelocity=hero.current_velocity;targetFalling=hero.cState.falling;targetGrounded=hero.cState.onGround;}
            catch(Exception error){Fail(error);}
        }
        private static void PositionRead(Vector3 __result)
        {if(Active){readFrame=Time.frameCount;readPosition=__result;}}
        private static void Frame(CameraController __instance)
        {
            if(!Active||Time.unscaledTime<nextSample)return;
            nextSample=Time.unscaledTime+.25f;
            try{EnsureScene();Snapshot(__instance,"SAMPLE",false);}catch(Exception error){Fail(error);}
        }
        private static void Snapshot(CameraController camera,string reason,bool shapes)
        {
            var controller=MarioController.Instance;var hero=HeroController.SilentInstance;
            if(hero==null||hero.cState==null)return;
            Vector3 cameraPosition=camera.transform.position,heroPosition=hero.transform.position;
            bool drop=haveSample&&cameraPosition.y<lastCameraY-.15f&&Mathf.Abs(heroPosition.y-lastHeroY)<.08f;
            if(reason=="SAMPLE"){lastCameraY=cameraPosition.y;lastHeroY=heroPosition.y;haveSample=true;}
            var target=camera.camTarget;var flags=hero.cState;
            string area="none";var selected=currentArea.GetValue(camera) as CameraLockArea;if(selected!=null)area=Zone(selected);
            Write((drop?"STATIONARY_CAMERA_DROP ":"")+reason+" camera="+V(cameraPosition)+" cameraMode="+camera.mode
                +" destination="+V(camera.destination)+" target="+(target!=null?V(target.transform.position):"none")
                +" targetMode="+(target!=null?target.mode.ToString():"none")+" lookOffset="+F(camera.lookOffset)
                +" yLimits="+F(camera.yLockMin)+".."+F(camera.yLockMax)
                +" projection="+(camera.cam!=null?(camera.cam.orthographic?"orthographic size="+F(camera.cam.orthographicSize):"perspective fov="+F(camera.cam.fieldOfView)):"none")
                +" fallCatcher="+(target!=null?F(target.fallCatcher):"none")+" fallStick="+(target!=null&&target.fallStick)
                +" hero="+V(heroPosition)+" heroEnabled="+hero.enabled+" grounded="+flags.onGround+" lookDown="+flags.lookingDown+" lookUp="+flags.lookingUp
                +" globalHero#="+Id(hero)+" cameraHero#="+Id(cameraHero.GetValue(camera) as UnityEngine.Object)+" targetHero#="+(target!=null?Id(target.hero_ctrl):0)
                +" nativeFalling="+flags.falling+" transitioning="+flags.transitioning
                +" targetInput(frame="+targetFrame+",falling="+targetFalling+",ground="+targetGrounded+",velocity="+targetVelocity.ToString("F3")+")"
                +" sampledHero(frame="+readFrame+",position="+V(readPosition)+")"
                +" mario="+(controller!=null?controller.Diagnostics+"; nativeFeet="+V(MarioMath.World(controller.State.px,controller.State.py,controller.State.pz))+"; view="+V(controller.ViewPosition):"none")+" selected="+area);
            if(shapes||drop)
            {
                foreach(var collider in hero.GetComponentsInChildren<Collider2D>(true))Write("PLAYER_SHAPE "+Shape(collider));
                if(drop&&Time.unscaledTime>=nextNearby)
                {
                    nextNearby=Time.unscaledTime+5f;
                    foreach(var zone in UnityEngine.Object.FindObjectsOfType<CameraLockArea>())
                    {
                        var bounds=zone.GetComponent<Collider2D>();
                        if(bounds!=null&&bounds.bounds.SqrDistance(new Vector3(heroPosition.x,heroPosition.y,bounds.bounds.center.z))<100f)Write("NEARBY_ZONE "+Zone(zone));
                    }
                }
            }
        }
        private static string Shape(Collider2D collider)
        {
            if(collider==null)return "none";
            var hero=HeroController.SilentInstance;var body=hero!=null?hero.GetComponent<BoxCollider2D>():null;
            string role=probe!=null&&probe(collider)?"interaction-probe":body!=null&&Id(collider)==Id(body)?"knight-solid-body":collider.CompareTag("HeroBox")?"knight-hurtbox":"other";
            var attached=collider.attachedRigidbody;
            return "id="+collider.GetInstanceID()+",role="+role+",name="+collider.name+",tag="+collider.tag+",layer="+collider.gameObject.layer
                +",trigger="+collider.isTrigger+",enabled="+collider.enabled+",bounds="+collider.bounds.ToString("F3")
                +",body="+Id(attached)+",bodyType="+(attached!=null?attached.bodyType.ToString():"none");
        }
        private static string Zone(CameraLockArea area)
        {
            if(area==null)return "none";var box=area.GetComponent<Collider2D>();
            return area.name+"#"+area.GetInstanceID()+" bounds="+(box!=null?box.bounds.ToString("F3"):"none")
                +" limits="+F(area.cameraXMin)+","+F(area.cameraXMax)+","+F(area.cameraYMin)+","+F(area.cameraYMax)+" priority="+area.maxPriority;
        }
        private static int Id(UnityEngine.Object value)=>value!=null?value.GetInstanceID():0;
        private static string F(float value)=>value.ToString("F3",CultureInfo.InvariantCulture);
        private static string V(Vector3 value)=>value.ToString("F3");
        private static string Caller()
        {
            var trace=new StackTrace(2,false);var names=new List<string>();
            for(int i=0;i<Math.Min(8,trace.FrameCount);i++){var method=trace.GetFrame(i).GetMethod();names.Add(method.DeclaringType?.FullName+"."+method.Name);}
            return string.Join(" > ",names.ToArray());
        }
        private static void EnsureScene()
        {
            int current=UnitySceneManager.GetActiveScene().handle;if(scene==current)return;
            scene=current;stays.Clear();haveSample=false;Write("SCENE "+UnitySceneManager.GetActiveScene().name+" build="+version);
        }
        private static void Write(string text)
        {
            if(writer==null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                writer=new StreamWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite));
                writer.WriteLine("SESSION "+DateTime.UtcNow.ToString("o")+" build="+version);
            }
            if(writer.BaseStream.Position>4*1024*1024)
            {
                writer.Dispose();writer=null;string previous=path+".previous";if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);
                writer=new StreamWriter(new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite));
                writer.WriteLine("ROTATED build="+version);
            }
            writer.WriteLine("frame="+Time.frameCount+" time="+F(Time.unscaledTime)+" "+text);writer.Flush();
        }
        private static void Fail(Exception error)
        {failed=true;lastError=error.ToString();try{writer?.Dispose();}catch{}writer=null;try{HostTrace.Sink?.Invoke("Camera diagnostics stopped: "+lastError);}catch{}}
        internal static void Dispose()
        {try{writer?.Dispose();}catch{}writer=null;enabled=null;contact=null;contactArea=null;contactPhase=null;}
    }
}
