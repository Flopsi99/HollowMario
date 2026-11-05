using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;
class WingsPickupChecks
{
 const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 sealed class Identity<T>:IEqualityComparer<T> where T:class {public bool Equals(T a,T b)=>ReferenceEquals(a,b);public int GetHashCode(T v)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v);}
 class FakeHero {public static Component instance;}
 class Variable {public GameObject Value;}
 class Variables {public Variable Cutscene;public Variable GetFsmGameObject(string name){Check(name=="Cutscene","Native cutscene variable is used");return Cutscene;}}
 class Fsm {public string Name="DJ Control";public GameObject GameObject;public Variables Variables;}
 class State {public Fsm Fsm;public string Name;}
 static Dictionary<GameObject,string> objectNames=new Dictionary<GameObject,string>(new Identity<GameObject>());
 static Dictionary<Component,Transform> transforms=new Dictionary<Component,Transform>(new Identity<Component>());
 static Dictionary<Transform,Vector3> positions=new Dictionary<Transform,Vector3>(new Identity<Transform>());
 static Dictionary<Component,GameObject> gameObjects=new Dictionary<Component,GameObject>(new Identity<Component>());
 static Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>(new Identity<Renderer>());
 static GameObject proxy;static MeshRenderer[] bodies;static Type presentation,conversation;static int checks,serial,starts,lastAnimation;static bool accepted=true;static float clock;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static T Empty<T>() where T:UnityEngine.Object {var obj=(T)FormatterServices.GetUninitializedObject(typeof(T));typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(obj,new IntPtr(++serial));return obj;}
 static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
 static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
 static string Name(GameObject go)=>objectNames[go];
 static GameObject ObjectOf(Component value)=>gameObjects[value];
 static Transform TransformOf(Component value)=>transforms[value];
 static Transform GameObjectTransform(GameObject value)=>ReferenceEquals(value,proxy)?proxyTransformValue:(Transform)FakeHero.instance;
 static Vector3 Position(Transform value)=>positions[value];
 static bool Active(GameObject value)=>true;
 static float TimeNow()=>clock;
 static BoxCollider2D Box(Component value)=>null;
 static MeshRenderer[] Renderers(GameObject value,bool includeInactive){Check(ReferenceEquals(value,proxy)&&includeInactive,"Only the cutscene double's renderer hierarchy is covered");return bodies;}
 static bool Hidden(Renderer value)=>hidden[value];
 static void Hide(Renderer value,bool off){hidden[value]=off;}
 static bool Capture(Component current,int clip,float yaw,bool holdControl,string group,ref bool __result){
  Check(ReferenceEquals(current,FakeHero.instance)&&Math.Abs(yaw)<.001f&&holdControl&&group=="monarch-pickup","Dedicated camera-facing pickup uses the existing presentation/control gate");
  starts++;lastAnimation=clip;__result=accepted;
  if(accepted){conversation.GetField("actionPose",A).SetValue(null,true);conversation.GetField("actionGroup",A).SetValue(null,group);conversation.GetField("actionHeld",A).SetValue(null,true);}return false;
 }
 static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code){foreach(var ins in code){
  var m=ins.operand as MethodInfo;string name=null;
  if(m!=null){
   if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Equality")name="Equal";
   else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Inequality")name="Unequal";
   else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_name")name="Name";
   else if(m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="get_name")name="Name";
   else if(m.DeclaringType==typeof(Component)&&m.Name=="get_transform")name="TransformOf";
   else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_transform")name="GameObjectTransform";
   else if(m.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")name="ObjectOf";
   else if(m.DeclaringType==typeof(Transform)&&m.Name=="get_position")name="Position";
   else if(m.DeclaringType==typeof(GameObject)&&m.Name=="get_activeInHierarchy")name="Active";
   else if(m.DeclaringType==typeof(Time)&&m.Name=="get_time")name="TimeNow";
   else if(m.DeclaringType==typeof(Renderer)&&m.Name=="get_forceRenderingOff")name="Hidden";
   else if(m.DeclaringType==typeof(Renderer)&&m.Name=="set_forceRenderingOff")name="Hide";
   else if(m.DeclaringType==typeof(GameObject)&&m.Name=="GetComponentsInChildren"&&m.IsGenericMethod)name="Renderers";
   else if(m.DeclaringType==typeof(Component)&&m.Name=="GetComponent"&&m.IsGenericMethod)name="Box";
  }
  if(name!=null){ins.opcode=OpCodes.Call;ins.operand=typeof(WingsPickupChecks).GetMethod(name,A);}yield return ins;
 }}
 static object Sample(int stage,float age)=>presentation.GetMethod("At",A).Invoke(null,new object[]{stage,age});
 static int Field(object sample,string name)=>(int)sample.GetType().GetField(name,A).GetValue(sample);
 static void DispatchChecks(Assembly mod){
  presentation=mod.GetType("HollowMario.MonarchWingsMario",true);conversation=mod.GetType("HollowMario.ConversationMario",true);
  var phase=presentation.GetMethod("Phase",A);
  foreach(var row in new[]{new object[]{"Get1",1},new object[]{"Get2",2},new object[]{"Get End",3},new object[]{"Fall",4},new object[]{"Land",0},new object[]{"Return Control",0}})
   Check((int)phase.Invoke(null,new object[]{"DJ Control","Shiny Item DJ",row[0]})==(int)row[1],"Native wings phase: "+row[0]);
  Check((int)phase.Invoke(null,new object[]{"Other","Shiny Item DJ","Get1"})==-1&&(int)phase.Invoke(null,new object[]{"DJ Control","Other Pickup","Get1"})==-1,"Other FSMs and pickup actors remain outside the replacement");
  Check(Field(Sample(1,0),"Frame")==0&&Field(Sample(1,.1f),"Frame")==3&&Field(Sample(1,10),"Frame")==8,"Triple landing advances naturally only as far as arms-out");
  foreach(int stage in new[]{2,3})Check(Field(Sample(stage,20),"Animation")==0xC0&&Field(Sample(stage,20),"Frame")==8,"Native hold/end waits keep the arms spread");
  Check(Field(Sample(4,0),"Animation")==0x56&&Field(Sample(4,0),"Frame")==-1,"Native fall changes to normal falling playback");
  Assembly.LoadFrom(System.IO.Path.Combine(TestEnvironment.Managed,"PlayMaker.dll"));
  var h=new Harmony("wings-pickup-integration");var game=Assembly.LoadFrom(System.IO.Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));var heroType=game.GetType("HeroController");
  presentation.GetMethod("Arm",A).Invoke(null,new object[]{h,heroType});
  var enter=AccessTools.TypeByName("HutongGames.PlayMaker.FsmState").GetMethod("OnEnter",A);
  Check(Harmony.GetPatchInfo(enter).Prefixes.Any(p=>p.PatchMethod.DeclaringType==presentation),"Native state hook registered");
  Check(Harmony.GetPatchInfo(heroType.GetMethod("RegainControl",A)).Postfixes.Any(p=>p.PatchMethod.DeclaringType==presentation),"Native control-return cleanup registered");
  foreach(string name in new[]{"BeforeState","HideDouble","Forget","TrySample","get_Active","get_Anchor"})h.Patch(presentation.GetMethod(name,A),transpiler:new HarmonyMethod(typeof(WingsPickupChecks),nameof(Endpoints)));
  h.Patch(conversation.GetMethod("StartAction",A),prefix:new HarmonyMethod(typeof(WingsPickupChecks),nameof(Capture)));
  presentation.GetField("heroType",A).SetValue(null,typeof(FakeHero));
  FakeHero.instance=Empty<Transform>();var heroGo=Empty<GameObject>();gameObjects.Add(FakeHero.instance,heroGo);transforms.Add(FakeHero.instance,(Transform)FakeHero.instance);positions.Add((Transform)FakeHero.instance,new Vector3(3,7,0));
  proxy=Empty<GameObject>();objectNames.Add(proxy,"Knight Cutscene");var proxyTransform=Empty<Transform>();transforms.Add(proxyTransform,proxyTransform);gameObjects.Add(proxyTransform,proxy);positions.Add(proxyTransform,new Vector3(3,9,0));
  proxyTransformValue=proxyTransform;
  bodies=new[]{Empty<MeshRenderer>(),Empty<MeshRenderer>()};hidden.Add(bodies[0],false);hidden.Add(bodies[1],true);
  var owner=Empty<GameObject>();objectNames.Add(owner,"Shiny Item DJ");var fsm=new Fsm{GameObject=owner,Variables=new Variables{Cutscene=new Variable{Value=proxy}}};var state=new State{Fsm=fsm,Name="Get1"};
  var before=presentation.GetMethod("BeforeState",A);clock=0;before.Invoke(null,new object[]{state});
  Check(lastAnimation==0xC0&&hidden[bodies[0]]&&hidden[bodies[1]],"Get1 replaces and hides the separate wing-spreading body");
  Check((Vector3)presentation.GetProperty("Anchor",A).GetValue(null,null)==new Vector3(3,9,0),"Pose follows the native cutscene double before the fall");
  foreach(string next in new[]{"Get2","Get End"}){state.Name=next;before.Invoke(null,new object[]{state});Check(lastAnimation==0xC0&&hidden[bodies[0]],"Hold survives native mesh-enable actions: "+next);}
  Check((bool)conversation.GetMethod("ScriptedPickup",A).Invoke(null,new object[]{"Collect Magical 1"}),"Generic pickup dance cannot take over the wings sequence");
  state.Name="Fall";before.Invoke(null,new object[]{state});Check(lastAnimation==0x56&&(Vector3)presentation.GetProperty("Anchor",A).GetValue(null,null)==new Vector3(3,7,0),"Fall follows the real Hero rather than the abandoned double");
  state.Name="Land";before.Invoke(null,new object[]{state});
  Check(!(bool)presentation.GetProperty("OwnsSequence",A).GetValue(null,null)&&!hidden[bodies[0]]&&hidden[bodies[1]]&&!(bool)conversation.GetField("actionHeld",A).GetValue(null),"Landing releases ownership and restores each renderer's original hidden flag");
  accepted=false;state.Name="Get1";before.Invoke(null,new object[]{state});Check(!(bool)presentation.GetProperty("OwnsSequence",A).GetValue(null,null)&&!hidden[bodies[0]],"A rejected/non-Mario presentation leaves the native double untouched");accepted=true;
  before.Invoke(null,new object[]{state});presentation.GetMethod("AfterControl",A).Invoke(null,new object[]{FakeHero.instance});
  Check(!(bool)presentation.GetProperty("OwnsSequence",A).GetValue(null,null)&&!hidden[bodies[0]],"An early native control return also restores the double and releases the hold");
  int oldStarts=starts;fsm.Name="Enemy Control";before.Invoke(null,new object[]{state});Check(starts==oldStarts,"Unrelated state events never start the pickup presentation");
  h.UnpatchSelf();
 }
 static Transform proxyTransformValue;
 static int Main(string[] args){try{
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);DispatchChecks(mod);
  TestEnvironment.StartNative();var host=Assembly.LoadFrom(TestEnvironment.HostPath);var native=host.GetType("MarioHollow.Native");
  var flourish=mod.GetType("HollowMario.MapFallFlourish");
  flourish.GetMethod("Configure",A).Invoke(null,new object[]{native,new BepInEx.Logging.ManualLogSource("wings-pickup")});
  var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);
  var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}
  object[] audio={0u,1088u,new short[8192]};
  using(var pose=new OpeningMarioPose(native,TestEnvironment.Library,0f,true)){
   for(int frame=0;frame<120;frame++){
    var sample=Sample(frame<40?1:frame<80?2:frame<110?3:4,frame/30f);int clip=Field(sample,"Animation"),held=Field(sample,"Frame");
    pose.TalkFacing(0f);int prior=(int)flourish.GetMethod("PushCinematicAnimation",A).Invoke(null,new object[]{clip});
    try{if(held>=0){if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(held-1));}b=pose.Tick(b,true);}
    finally{flourish.GetMethod("PopCinematicAnimation",A).Invoke(null,new object[]{prior});}TestEnvironment.PullAudio(audio);
    int count=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));var v=new float[count*9];Marshal.Copy((IntPtr)bt.GetField("position").GetValue(b),v,0,v.Length);
    float feet=Convert.ToSingle(pose.State.GetType().GetField("py").GetValue(pose.State)),min=float.PositiveInfinity,max=float.NegativeInfinity;
    for(int j=0;j<v.Length;j+=3)if(v[j+1]>feet+70){min=Math.Min(min,v[j]);max=Math.Max(max,v[j]);}
    Check(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))==clip,"Real ROM animation matches the native pickup phase");
    if(frame>=10&&frame<110)Check(Convert.ToInt32(pose.State.GetType().GetField("animFrame").GetValue(pose.State))==8&&max-min>140,"Actual arms-out frame holds without finishing or looping the landing");
   }
  }
  foreach(var pin in pins)pin.Free();Console.WriteLine("PASS "+checks+" Monarch Wings checks: scoped native state hooks, cutscene-double visibility/restoration, native fall/control handoff and ROM-backed arms-out hold. No game window was opened; final visual timing needs a playtest.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
