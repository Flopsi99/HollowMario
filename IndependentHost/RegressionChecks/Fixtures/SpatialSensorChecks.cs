using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;
class SpatialSensorChecks {
 const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Collider2D sensor,body;static GameObject root,child;static string zone;static PlayMakerFSM visualFsm;static int locks,releases;
 class Named {public string Name;public Named(string name){Name=name;}}
 class ActionContext {public object Fsm;public ActionContext(string name){Fsm=new Named(name);}}
 static void Check(bool yes,string why){if(!yes)throw new Exception(why);}
 static object Empty(Type type)=>FormatterServices.GetUninitializedObject(type);
 static bool Applicable(ref bool __result){__result=true;return false;}
 static bool Tagged(Component c,string tag)=>tag=="Player"&&ReferenceEquals(c,body);
 static GameObject ObjectOf(Component c)=>ReferenceEquals(c,body)?root:child;
 static Transform TransformOf(GameObject g)=>null;
 static Vector3 PositionOf(Transform t)=>Vector3.zero;
 static bool Active(Behaviour b)=>true;
 static void Lock(CameraController c,CameraLockArea area){locks++;}
 static void Release(CameraController c,CameraLockArea area){releases++;}
 static PlayMakerFSM[] Fsms(Component c)=>new[]{visualFsm};
 static bool FsmName(ref string __result){__result=zone;return false;}
 static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> instructions){
  foreach(var i in instructions){if(i.operand is MethodInfo m){string name=null;
   if(m.DeclaringType==typeof(Component)&&m.Name=="CompareTag")name="Tagged";
   if(m.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")name="ObjectOf";
   if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_transform")name="TransformOf";
   if(m.DeclaringType==typeof(Transform)&&m.Name=="get_position")name="PositionOf";
   if(m.DeclaringType==typeof(Behaviour)&&m.Name=="get_isActiveAndEnabled")name="Active";
   if(m.DeclaringType==typeof(CameraController)&&m.Name=="LockToArea")name="Lock";
   if(m.DeclaringType==typeof(CameraController)&&m.Name=="ReleaseLock")name="Release";
   if(m.IsGenericMethod&&m.Name=="GetComponents"&&m.GetGenericArguments()[0]==typeof(PlayMakerFSM))name="Fsms";
   if(name!=null){i.opcode=OpCodes.Call;i.operand=typeof(SpatialSensorChecks).GetMethod(name,A);}
  }yield return i;}
 }
 static int Main(string[] args){try{
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var host=Assembly.LoadFrom(TestEnvironment.HostPath);
  var plugin=mod.GetType("HollowMario.Plugin",true);var bridge=host.GetType("MarioHollow.BridgeContracts",true);
  var h=new Harmony("probe-isolation-checks");var endpoints=new HarmonyMethod(typeof(SpatialSensorChecks),nameof(Endpoints));
  sensor=(Collider2D)Empty(typeof(BoxCollider2D));body=(Collider2D)Empty(typeof(BoxCollider2D));
  var alias=(Collider2D)Empty(typeof(BoxCollider2D));var retired=(Collider2D)Empty(typeof(BoxCollider2D));
  root=(GameObject)Empty(typeof(GameObject));child=(GameObject)Empty(typeof(GameObject));
  var pointer=typeof(UnityEngine.Object).GetField("m_CachedPtr",A);var offset=typeof(UnityEngine.Object).GetField("OffsetOfInstanceIDInCPlusPlusObject",A);
  object oldOffset=offset.GetValue(null);offset.SetValue(null,0);
  IntPtr currentRecord=Marshal.AllocHGlobal(4),bodyRecord=Marshal.AllocHGlobal(4),oldRecord=Marshal.AllocHGlobal(4);
  Marshal.WriteInt32(currentRecord,101);Marshal.WriteInt32(bodyRecord,202);Marshal.WriteInt32(oldRecord,303);
  pointer.SetValue(sensor,currentRecord);pointer.SetValue(alias,currentRecord);pointer.SetValue(body,bodyRecord);pointer.SetValue(retired,oldRecord);
  plugin.GetField("feetPad",A).SetValue(null,sensor);plugin.GetField("feetPadBody",A).SetValue(null,body);plugin.GetField("feetPadId",A).SetValue(null,101);
  plugin.GetField("retiredFeetPad",A).SetValue(null,retired);plugin.GetField("retiredFeetPadBody",A).SetValue(null,body);plugin.GetField("retiredFeetPadId",A).SetValue(null,303);
  foreach(var c in new[]{sensor,alias,retired})Check((bool)plugin.GetMethod("IsFeetPad",A).Invoke(null,new object[]{c}),"Native sensor identity including wrapper/retired contact");
  Check(!(bool)plugin.GetMethod("IsFeetPad",A).Invoke(null,new object[]{body}),"Physical body is distinct");
  h.Patch(AccessTools.Method(typeof(CameraLockArea),"IsInApplicableGameState"),prefix:new HarmonyMethod(typeof(SpatialSensorChecks),nameof(Applicable)));
  var area=(CameraLockArea)Empty(typeof(CameraLockArea));
  // First exercise the game's actual camera scripts WITHOUT our camera filters.
  // The only variable is the incoming object's Player/Untagged identity.
  foreach(string phase in new[]{"Enter","Stay","Exit"}){
   var callback=AccessTools.Method(typeof(CameraLockArea),"OnTrigger"+phase+"2D");h.Patch(callback,transpiler:endpoints);
   int before=locks+releases;callback.Invoke(area,new object[]{sensor});Check(locks+releases==before,"Untagged probe cannot select/release a lower room through native "+phase);
   callback.Invoke(area,new object[]{body});Check(locks+releases==before+1,"The real Player still selects/releases native camera regions on "+phase);
  }
  using(var source=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath)){
   var create=source.MainModule.GetType("HollowMario.InteractionProbe").Methods;bool untagged=false,separateBody=false,childObject=false;
   foreach(var m in create)if(m.Name=="Create")foreach(var i in m.Body.Instructions){
    if(i.OpCode.Code==Mono.Cecil.Cil.Code.Ldstr&&Convert.ToString(i.Operand)=="Untagged")untagged=true;
    if(i.Operand is GenericInstanceMethod call&&call.Name=="AddComponent"&&call.GenericArguments[0].Name=="Rigidbody2D")separateBody=true;
    if(i.Operand is MethodReference constructor&&i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&constructor.DeclaringType.Name=="GameObject")childObject=true;
   }
   Check(untagged&&separateBody&&childObject,"Packaged factory removes Player tag and shared physics ownership instead of adding another collider to Knight");
  }
  bridge.GetMethod("Install",A).Invoke(null,new object[]{mod});
  foreach(string phase in new[]{"Enter","Stay","Exit"}){
   var callback=AccessTools.Method(typeof(HutongGames.PlayMaker.Fsm),"OnTrigger"+phase+"2D");bool found=false;
   foreach(var p in Harmony.GetPatchInfo(callback).Prefixes)if(p.PatchMethod.DeclaringType==bridge)found=true;
   Check(found,"Automatic FSM trigger route is covered, not only individual actions: "+phase);
  }
  foreach(string name in new[]{"Camera Control","Darkness Region","unmasker","remasker","Roam Range","Control"})foreach(string method in new[]{"BeforeTrigger","BeforeSystemTrigger"}){
   object context=method=="BeforeTrigger"?(object)new ActionContext(name):new Named(name);object[] call={context,alias};
   Check(!(bool)bridge.GetMethod(method,A).Invoke(null,call)&&ReferenceEquals(call[1],alias),"Probe cannot gain Player identity in "+name);
  }
  foreach(string name in new[]{"npc_control","Shop Region","Inspection","inspect","Stag Bell","Stag Control","Bench Control","Door Control","Quake Floor","collapse small","collapse tute"})foreach(string method in new[]{"BeforeTrigger","BeforeSystemTrigger"}){
   object context=method=="BeforeTrigger"?(object)new ActionContext(name):new Named(name);object[] call={context,alias};
   Check((bool)bridge.GetMethod(method,A).Invoke(null,call)&&ReferenceEquals(call[1],body),"Approved interaction receives real hero identity: "+name);
  }
  visualFsm=(PlayMakerFSM)Empty(typeof(PlayMakerFSM));var tracker=Empty(typeof(TrackTriggerObjects));
  h.Patch(typeof(PlayMakerFSM).GetProperty("FsmName",A).GetGetMethod(true),prefix:new HarmonyMethod(typeof(SpatialSensorChecks),nameof(FsmName)));
  h.Patch(bridge.GetMethod("BeforeTrackedContact",A),transpiler:endpoints);
  zone="npc_control";object[] entry={tracker,alias};Check((bool)bridge.GetMethod("BeforeTrackedContact",A).Invoke(null,entry)&&ReferenceEquals(entry[1],body),"Tracker receives one hero, not a second child object");
  zone="Darkness Region";entry=new object[]{tracker,alias};Check(!(bool)bridge.GetMethod("BeforeTrackedContact",A).Invoke(null,entry),"Probe is excluded from visual occupancy");
  h.Patch(bridge.GetMethod("TrackedObject",A),transpiler:endpoints);
  Check(ReferenceEquals(bridge.GetMethod("TrackedObject",A).Invoke(null,new object[]{alias}),root),"Native overlap refresh maps the child probe to the same hero as enter/exit callbacks");
  bool refresh=false;foreach(var p in Harmony.GetPatchInfo(AccessTools.Method(typeof(TrackTriggerObjects),"GetOverlappedColliders")).Transpilers)if(p.PatchMethod.DeclaringType==bridge)refresh=true;
  Check(refresh,"Actual overlap refresh uses that identity mapping");
  bridge.GetMethod("Dispose",A).Invoke(null,null);h.UnpatchSelf();offset.SetValue(null,oldOffset);
  Marshal.FreeHGlobal(currentRecord);Marshal.FreeHGlobal(bodyRecord);Marshal.FreeHGlobal(oldRecord);
  Console.WriteLine("PASS native camera scripts: old Player contact locks/releases, untagged probe never does even without camera filters. Separate-body factory; approved NPC/shop/bench/floor routes get one hero; generic FSM and overlap-refresh identities covered. Scene endpoints simulated; live Unity physics not tested.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
