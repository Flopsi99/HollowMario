// Execute HK's installed camera methods. Only Unity scene/time endpoints are
// substituted: no copy or reimplementation of the camera's follow/lock algorithm.
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;
class CameraParityChecks {
 const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 class Identity<T>:IEqualityComparer<T> where T:class {public bool Equals(T a,T b)=>ReferenceEquals(a,b);public int GetHashCode(T value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}
 static readonly Dictionary<Component,Transform> transforms=new Dictionary<Component,Transform>(new Identity<Component>());
 static readonly Dictionary<Transform,Vector3> positions=new Dictionary<Transform,Vector3>(new Identity<Transform>());
 static readonly Dictionary<Component,GameObject> objects=new Dictionary<Component,GameObject>(new Identity<Component>());
 static readonly Dictionary<GameObject,Transform> objectTransforms=new Dictionary<GameObject,Transform>(new Identity<GameObject>());
 static int serial,frame,updates;static Collider2D pad;
 static readonly Dictionary<Collider2D,Bounds> colliderBounds=new Dictionary<Collider2D,Bounds>(new Identity<Collider2D>());
 static Bounds BoundsOf(Collider2D value)=>colliderBounds[value];
 static Type view,look;static MethodInfo beforeCamera;static MarioController mario;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static T Empty<T>() where T:UnityEngine.Object {
  var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
  typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(value,new IntPtr(++serial));
  return value;
 }
 static void Set(object target,string name,object value){var f=target.GetType().GetField(name,A);if(f!=null)f.SetValue(target,value);else target.GetType().GetProperty(name,A).SetValue(target,value,null);}
 static Transform TransformOf(Component value)=>transforms[value];
 static Transform ObjectTransform(GameObject value)=>objectTransforms[value];
 static GameObject GameObjectOf(Component value)=>objects[value];
 static Vector3 Position(Transform value)=>positions[value];
 static void Position(Transform value,Vector3 point){positions[value]=point;}
 static void SetX(Transform value,float x){var p=positions[value];p.x=x;positions[value]=p;}
 static void SetY(Transform value,float y){var p=positions[value];p.y=y;positions[value]=p;}
 static bool Tagged(Component value,string tag)=>tag=="Player";
 static bool Active(Behaviour value)=>true;
 static bool Enabled(Behaviour value)=>false;
 static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
 static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
 static bool Contains(List<CameraLockArea> list,CameraLockArea area)=>list.Exists(item=>ReferenceEquals(item,area));
 static bool Remove(List<CameraLockArea> list,CameraLockArea area){int index=list.FindIndex(item=>ReferenceEquals(item,area));if(index<0)return false;list.RemoveAt(index);return true;}
 static float Delta()=>1f/60f;
 static int Frame()=>frame;
 static float Unscaled()=>frame/60f;
 static int ObjectId(UnityEngine.Object value)=>ReferenceEquals(value,null)?0:(int)((IntPtr)typeof(UnityEngine.Object).GetField("m_CachedPtr",A).GetValue(value)).ToInt64();
 static bool SkipDiagnosticScene()=>false;
 static HeroController NoSceneHero()=>null;
 static bool DiagnosticShape(Collider2D collider,ref string __result){__result="fixture-body-"+ObjectId(collider);return false;}
 static bool DiagnosticZone(CameraLockArea area,ref string __result){__result="fixture-zone-"+ObjectId(area);return false;}
 static Vector3 Smooth(Vector3 from,Vector3 to,ref Vector3 speed,float time)=>Vector3.SmoothDamp(from,to,ref speed,time,float.PositiveInfinity,Delta());
 static bool Applicable(ref bool __result){__result=true;return false;}
 static void CountUpdate(){updates++;}
 static bool IsPad(Collider2D value)=>ReferenceEquals(value,pad);
 static MethodInfo Own(string name,params Type[] args)=>typeof(CameraParityChecks).GetMethod(name,A,null,args,null);
 static IEnumerable<CodeInstruction> SceneEndpoints(IEnumerable<CodeInstruction> code){
  foreach(var ins in code){
   if(ins.operand is MethodInfo m){MethodInfo replace=null;
    if(m.DeclaringType==typeof(Component)&&m.Name=="get_transform")replace=Own("TransformOf",typeof(Component));
    else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_transform")replace=Own("ObjectTransform",typeof(GameObject));
    else if(m.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")replace=Own("GameObjectOf",typeof(Component));
    else if(m.DeclaringType==typeof(Transform)&&m.Name=="get_position")replace=Own("Position",typeof(Transform));
    else if(m.DeclaringType==typeof(Transform)&&m.Name=="set_position")replace=Own("Position",typeof(Transform),typeof(Vector3));
    else if(m.Name=="SetPositionX")replace=Own("SetX",typeof(Transform),typeof(float));
    else if(m.Name=="SetPositionY")replace=Own("SetY",typeof(Transform),typeof(float));
    else if(m.Name=="CompareTag"&&m.DeclaringType==typeof(Component))replace=Own("Tagged",typeof(Component),typeof(string));
    else if(m.Name=="get_isActiveAndEnabled"&&m.DeclaringType==typeof(Behaviour))replace=Own("Active",typeof(Behaviour));
    else if(m.Name=="get_enabled"&&m.DeclaringType==typeof(Behaviour))replace=Own("Enabled",typeof(Behaviour));
    else if(m.DeclaringType==typeof(Collider2D)&&m.Name=="get_bounds")replace=Own("BoundsOf",typeof(Collider2D));
    else if(m.DeclaringType==typeof(Time)&&m.Name=="get_deltaTime")replace=Own("Delta");
    else if(m.DeclaringType==typeof(Time)&&m.Name=="get_frameCount")replace=Own("Frame");
    else if(m.DeclaringType==typeof(Time)&&m.Name=="get_unscaledTime")replace=Own("Unscaled");
    else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="GetInstanceID")replace=Own("ObjectId",typeof(UnityEngine.Object));
    else if(m.DeclaringType==typeof(HeroController)&&m.Name=="get_SilentInstance")replace=Own("NoSceneHero");
    else if(m.DeclaringType==typeof(Vector3)&&m.Name=="SmoothDamp"&&m.GetParameters().Length==4)replace=Own("Smooth",typeof(Vector3),typeof(Vector3),typeof(Vector3).MakeByRefType(),typeof(float));
    else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Equality")replace=Own("Equal",typeof(UnityEngine.Object),typeof(UnityEngine.Object));
    else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Inequality")replace=Own("Unequal",typeof(UnityEngine.Object),typeof(UnityEngine.Object));
    else if(m.DeclaringType==typeof(List<CameraLockArea>)&&m.Name=="Contains")replace=Own("Contains",typeof(List<CameraLockArea>),typeof(CameraLockArea));
    else if(m.DeclaringType==typeof(List<CameraLockArea>)&&m.Name=="Remove")replace=Own("Remove",typeof(List<CameraLockArea>),typeof(CameraLockArea));
    if(replace!=null){ins.opcode=OpCodes.Call;ins.operand=replace;}
   }
   yield return ins;
  }
 }
 static Transform Locate(Component component,Vector3 point){var tr=Empty<Transform>();transforms[component]=tr;positions[tr]=point;return tr;}
 struct Snapshot {public Vector3 Target,Camera;public CameraTarget.TargetMode Mode;public float Catcher;}
 static float Height(int tick){if(tick<10)return 100f;if(tick<40)return 100f+(tick-10)/3f;if(tick<170)return 110f-(tick-40)*(100f/130f);if(tick<210)return 10f;if(tick<240)return 10f+(tick-210)*(20f/30f);return Mathf.Max(10f,30f-(tick-240)*(20f/60f));}
 static Snapshot[] Run(bool playingMario,bool renderFirst){
  var hero=Empty<HeroController>();hero.cState=new HeroControllerStates();hero.transitionState=GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION;
  var heroTr=Locate(hero,new Vector3(40,100,0));
  var target=Empty<CameraTarget>();var targetTr=Locate(target,new Vector3(40,100,0));
  var camera=Empty<CameraController>();var cameraTr=Locate(camera,new Vector3(40,100,0));
  var gm=Empty<GameManager>();Set(gm,"gameState",GlobalEnums.GameState.PLAYING);
  Set(target,"heroTransform",heroTr);Set(target,"isGameplayScene",true);Set(target,"snapDistance",.15f);
  target.hero_ctrl=hero;target.cameraCtrl=camera;target.mode=CameraTarget.TargetMode.FOLLOW_HERO;
  target.dampTimeNormal=.15f;target.dampTimeSlow=.3f;target.xLookAhead=1f;
  target.xLockMax=200f;target.yLockMax=200f;target.stickToHeroX=target.stickToHeroY=true;
  camera.camTarget=target;camera.lockZoneList=new List<CameraLockArea>();camera.xLimit=camera.yLimit=200f;Set(camera,"hero_ctrl",hero);Set(camera,"gm",gm);
  var body=Empty<BoxCollider2D>();var go=Empty<GameObject>();objects[body]=go;objectTransforms[go]=heroTr;
  CameraLockArea Zone(float min,float max){var zone=Empty<CameraLockArea>();zone.cameraXMin=14.6f;zone.cameraXMax=180f;zone.cameraYMin=min;zone.cameraYMax=max;
   Set(zone,"cameraCtrl",camera);Set(zone,"camTarget",target);Set(zone,"box2d",Empty<BoxCollider2D>());return zone;}
  var upper=Zone(80,180);var lower=Zone(8.3f,90);
  mario.hero=hero;mario.MarioId=-1;
  upper.OnTriggerEnter2D(body);Check(camera.lockZoneList.Count==1,"Entry during native control acquires its real camera lock");
  if(playingMario){view.GetMethod("Forget",A).Invoke(null,null);mario.Renderer=null;}
  var result=new Snapshot[600];
  for(int n=0;n<result.Length;n++){
   frame=n+1;int tick=n/2;float alpha=(n%2)*.5f;
   float previous=Height(Math.Max(0,tick-1)),current=Height(tick),y=previous+(current-previous)*alpha;
   float speed=current-previous;bool airborne=tick>=10&&tick<170||tick>=210&&tick<300;
   hero.cState.onGround=!airborne;hero.cState.facingRight=tick<210;hero.cState.falling=playingMario?false:airborne&&speed<0;
   Set(hero,"controlReqlinquished",n>=250&&n<270);hero.current_velocity=new Vector2(-99,-99);
   mario.MarioId=playingMario&&n>=20?7:-1;
   mario.PrevState=new SM64MarioState{px=40/MarioMath.Scale,py=(previous-1f)/MarioMath.Scale};
   mario.State=new SM64MarioState{px=40/MarioMath.Scale,py=(current-1f)/MarioMath.Scale,vy=speed/MarioMath.Scale,action=airborne?0x0100088Cu:0x0C400201u};
   Set(mario,"accumulator",alpha/30f);Set(mario,"feetOffset",1f);
   positions[heroTr]=new Vector3(40,playingMario&&n>=20?current:y,0);
   if(n==150)lower.OnTriggerEnter2D(body);
   if(n==180){upper.OnTriggerExit2D(body);Check(!Contains(camera.lockZoneList,upper),"A lock acquired before Mario takes control exits after the handover");}
   if(n==350){lower.OnTriggerExit2D(body);Check(camera.lockZoneList.Count==0&&target.mode==CameraTarget.TargetMode.FOLLOW_HERO,"Last native lock exit restores ordinary following");}
   if(playingMario&&n>=20){
    // Deliberately vary render order and leave a wrong/stale mesh sample. The
    // actual BeforeCamera must use the host's live interpolation property.
    view.GetField("renderedFrame",A).SetValue(null,renderFirst?frame:frame-1);
    view.GetField("renderedHero",A).SetValue(null,new Vector3(40,current+5f,0));
    beforeCamera.Invoke(null,new object[]{camera});int calls=updates;beforeCamera.Invoke(null,new object[]{camera});Check(updates==calls,"Camera target advances once per frame");
    Check(!hero.cState.falling&&hero.current_velocity==new Vector2(-99,-99),"Actual camera prefix/finalizer restores host motion flags and velocity");
   }else target.Update();
   result[n]=new Snapshot{Target=positions[targetTr],Camera=positions[cameraTr],Mode=target.mode,Catcher=target.fallCatcher};
  }
  Check(result[599].Camera.y<20f&&result[599].Target.y<20f,"Long drop finishes following the player at ground level");
  return result;
 }
 static void CheckContactGap(){
  var hero=Empty<HeroController>();hero.cState=new HeroControllerStates();var heroTr=Locate(hero,new Vector3(156.225f,45.392f,0));
  var camera=Empty<CameraController>();Locate(camera,new Vector3(156,45,-38));
  var target=Empty<CameraTarget>();Locate(target,new Vector3(156,45,0));
  var gm=Empty<GameManager>();Set(gm,"gameState",GlobalEnums.GameState.PLAYING);
  camera.camTarget=target;camera.lockZoneList=new List<CameraLockArea>();camera.xLimit=camera.yLimit=200;Set(camera,"hero_ctrl",hero);Set(camera,"gm",gm);
  target.hero_ctrl=hero;target.cameraCtrl=camera;Set(target,"heroTransform",heroTr);Set(target,"isGameplayScene",true);target.dampTimeNormal=.15f;
  var body=Empty<BoxCollider2D>();var go=Empty<GameObject>();objects[body]=go;objectTransforms[go]=heroTr;
  var area=Empty<CameraLockArea>();var region=Empty<BoxCollider2D>();Set(area,"box2d",region);Set(area,"cameraCtrl",camera);Set(area,"camTarget",target);
  area.cameraXMin=14.6f;area.cameraXMax=186.4f;area.cameraYMin=area.cameraYMax=38f;
  colliderBounds[region]=new Bounds(new Vector3(180.037f,39.888f,100),new Vector3(47.278f,8.190f,0));
  colliderBounds[body]=new Bounds(new Vector3(156.225f,44.642f,0),new Vector3(.5f,1.282f,0));
  mario.hero=hero;mario.MarioId=-1;Set(mario,"box",body);
  area.OnTriggerEnter2D(body);Check(Contains(camera.lockZoneList,area),"Native trigger alone reproduces the logged lower-zone acquisition across a 0.018-unit gap");
  mario.MarioId=7;area.OnTriggerStay2D(body);Check(camera.lockZoneList.Count==0,"Mario releases a gap-only lock acquired before handover");
  area.OnTriggerEnter2D(body);for(int i=0;i<3;i++)area.OnTriggerStay2D(body);
  Check(camera.lockZoneList.Count==0,"Logged tutorial contact gap never acquires/reacquires the lower camera zone");
  colliderBounds[body]=new Bounds(new Vector3(156.225f,43.9f,0),new Vector3(.5f,1.282f,0));
  area.OnTriggerEnter2D(body);Check(Contains(camera.lockZoneList,area),"Actual overlap still acquires the authored lock, independently of rendering depth");
  var upper=Empty<CameraLockArea>();var upperRegion=Empty<BoxCollider2D>();Set(upper,"box2d",upperRegion);Set(upper,"cameraCtrl",camera);Set(upper,"camTarget",target);
  upper.cameraXMin=14.6f;upper.cameraXMax=186.4f;upper.cameraYMin=40;upper.cameraYMax=80;
  colliderBounds[upperRegion]=new Bounds(new Vector3(156,50,0),new Vector3(10,40,0));upper.OnTriggerEnter2D(body);
  Check(camera.lockZoneList.Count==2,"Genuinely overlapping locks retain native bookkeeping");
  colliderBounds[body]=new Bounds(new Vector3(156.225f,44.642f,0),new Vector3(.5f,1.282f,0));area.OnTriggerStay2D(body);
  Check(camera.lockZoneList.Count==1&&Contains(camera.lockZoneList,upper),"Losing overlap releases only that lock and preserves the other native lock");
  area.OnTriggerExit2D(body);upper.OnTriggerExit2D(body);Check(camera.lockZoneList.Count==0,"Real exit callbacks remain accepted after a filtered stay");
  colliderBounds[body]=new Bounds(new Vector3(156.148f,39,0),new Vector3(.5f,1,0));
  Check(!BridgeContracts.CameraBoundsOverlap(colliderBounds[region],colliderBounds[body]),"Side-edge contact without positive overlap is excluded too");
  Set(mario,"box",null);mario.MarioId=-1;
 }
 static int Main(string[] args){try{
  var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);var h=new Harmony("camera-native-parity");
  var endpoints=new HarmonyMethod(typeof(CameraParityChecks),nameof(SceneEndpoints)){priority=Priority.Last};
  foreach(string name in new[]{"Update","SetDampTime","EnterLockZone","EnterLockZoneInstant","ExitLockZone"})h.Patch(AccessTools.Method(typeof(CameraTarget),name),transpiler:endpoints);
  foreach(string name in new[]{"LockToArea","ReleaseLock"})h.Patch(AccessTools.Method(typeof(CameraController),name),transpiler:endpoints);
  foreach(string phase in new[]{"Enter","Stay","Exit"})h.Patch(AccessTools.Method(typeof(CameraLockArea),"OnTrigger"+phase+"2D"),transpiler:endpoints);
  h.Patch(AccessTools.Method(typeof(CameraLockArea),"IsInApplicableGameState"),prefix:new HarmonyMethod(typeof(CameraParityChecks),nameof(Applicable)));
  h.Patch(AccessTools.Method(typeof(CameraTarget),"Update"),postfix:new HarmonyMethod(typeof(CameraParityChecks),nameof(CountUpdate)));
  BridgeContracts.Install(companion);pad=Empty<BoxCollider2D>();typeof(BridgeContracts).GetField("supportSensor",A).SetValue(null,(Func<Collider2D,bool>)IsPad);
  h.Patch(typeof(BridgeContracts).GetMethod("CameraOverlapAllowed",A),transpiler:endpoints);
  mario=Empty<MarioController>();MarioController.Instance=mario;MarioMath.Origin=Vector2.zero;MarioMath.LaneZ=0;
  var baseline=Run(false,false);
  var production=new Harmony("camera-parity-production");
  var pluginType=companion.GetType("HollowMario.Plugin");object plugin=FormatterServices.GetUninitializedObject(pluginType);
  object logger=Activator.CreateInstance(pluginType.GetField("Log",A).FieldType,new object[]{"camera-parity"});Set(plugin,"Log",logger);
  object follow=Activator.CreateInstance(companion.GetType("HollowMario.KnightResetFollow"),A,null,new object[]{logger,typeof(MarioController),200},null);
  Set(plugin,"knightFollow",follow);pluginType.GetField("Instance",A).SetValue(null,plugin);
  look=companion.GetType("HollowMario.KnightLook",true);look.GetMethod("Arm",A).Invoke(null,new object[]{production,logger});
  h.Patch(look.GetMethod("BeforeTarget",A),transpiler:endpoints);
  view=companion.GetType("HollowMario.MarioView",true);view.GetMethod("Arm",A).Invoke(null,new object[]{production,typeof(MarioController),typeof(Native)});
  h.Patch(view.GetMethod("CanFollow",A),transpiler:endpoints);
  beforeCamera=view.GetMethod("BeforeCamera",A);h.Patch(beforeCamera,transpiler:endpoints);
  h.Patch(view.GetMethod("ReadHeroPosition",A),transpiler:endpoints);
  // After Arm, rematerialize the target with scene endpoints after its new
  // hero-position sampling transpiler, preserving the production sampling.
  h.Unpatch(AccessTools.Method(typeof(CameraTarget),"Update"),HarmonyPatchType.Transpiler,h.Id);
  h.Patch(AccessTools.Method(typeof(CameraTarget),"Update"),transpiler:endpoints);
  int compared=0;
  // Enable the real diagnostic hooks for both comparisons. Only scene lookups
  // and object descriptions are substituted; observers and file output run.
  var diagnostic=typeof(BridgeContracts).Assembly.GetType("MarioHollow.CameraTrace",true);
  string tracePath=Path.Combine(args[0],".artifacts/independent-host/regressions/camera-read-only-"+Guid.NewGuid().ToString("N")+".log");
  diagnostic.GetField("path",A).SetValue(null,tracePath);diagnostic.GetField("enabled",A).SetValue(null,(Func<bool>)(()=>true));
  diagnostic.GetField("probe",A).SetValue(null,(Func<Collider2D,bool>)IsPad);
  h.Patch(diagnostic.GetMethod("EnsureScene",A),prefix:new HarmonyMethod(typeof(CameraParityChecks),nameof(SkipDiagnosticScene)));
  h.Patch(diagnostic.GetMethod("Shape",A),prefix:new HarmonyMethod(typeof(CameraParityChecks),nameof(DiagnosticShape)));
  h.Patch(diagnostic.GetMethod("Zone",A),prefix:new HarmonyMethod(typeof(CameraParityChecks),nameof(DiagnosticZone)));
  foreach(string method in new[]{"BeforeContact","Write","Frame","TargetInput","PositionRead","Id","Snapshot"})h.Patch(diagnostic.GetMethod(method,A),transpiler:endpoints);
  foreach(bool order in new[]{false,true}){
   var actual=Run(true,order);
   for(int i=0;i<baseline.Length;i++){
    Check((baseline[i].Target-actual[i].Target).sqrMagnitude<.00001f,"Native Knight/Mario target trajectory diverged at frame "+i+", render first="+order);
    Check((baseline[i].Camera-actual[i].Camera).sqrMagnitude<.00001f&&baseline[i].Mode==actual[i].Mode&&Math.Abs(baseline[i].Catcher-actual[i].Catcher)<.001f,"Native fall catching/room lock diverged at frame "+i);compared++;
   }
  }
  Check(!(bool)diagnostic.GetField("failed",A).GetValue(null),"Enabled diagnostic observers never throw or disable themselves during native camera playback: "+diagnostic.GetField("lastError",A).GetValue(null));
  var writer=(StreamWriter)diagnostic.GetField("writer",A).GetValue(null);Check(writer!=null,"The trace really wrote data while enabled");
  writer.BaseStream.Position=4*1024*1024+1;
  diagnostic.GetMethod("Write",A).Invoke(null,new object[]{"rotation-check"});
  CheckContactGap();
  pluginType.GetField("Instance",A).SetValue(null,null);BridgeContracts.Dispose();production.UnpatchSelf();h.UnpatchSelf();MarioController.Instance=null;
  string trace=File.ReadAllText(tracePath+".previous");
  Check(trace.Contains("LOCK_CHANGE")&&trace.Contains("sourceCollider=fixture-body-")&&trace.Contains("sourcePhase=OnTriggerEnter2D"),"Actual native lock changes retain their originating collider and phase");
  Check(File.ReadAllText(tracePath).Contains("rotation-check"),"Trace output rotates and keeps the previous evidence file");
  Console.WriteLine("PASS "+compared+" comparisons against installed HK camera code: high drop, jump/apex/landing, overlapping lock entry/exit across control handover, both render orders and one target step per frame. Unity scene endpoints are simulated; no game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
