// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class ArrivalPoseChecks {
 class FakeHero {public static int Reads; public static object instance {get{Reads++;throw new Exception("Player should not be inspected");}}}
 class FakeFsm {public string Name="Enemy Control";}
 class FakeState {public FakeFsm Fsm=new FakeFsm();public string Name {get{throw new Exception("Enemy state details should not be inspected");}}}
 class FakeClip {public string name="Run";}

 class MapController {public static MapController Instance;public object hero;public int MarioId=-1;}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Volume(float volume);
 class Controller {public static Controller Instance;public object Renderer;}
 static int muted; static int lavaRequests;
 static void SoundObserved(uint __0,bool __result){if(__result)muted++;if((__0&0xF0FF0000u)==0x20140000u)lavaRequests++;}
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);
  string package=TestEnvironment.Package, managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var native=engine.GetType("MarioHollow.Native",true);
  string library=TestEnvironment.Library;TestEnvironment.StartNative();
  var audio=native.GetMethod("sm64_audio_tick");object[] audioArgs={0u,1088u,new short[8192]};Console.WriteLine("ROM and audio initialized");var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);var bench=companion.GetType("HollowMario.BenchMario",true);
  var flourish=companion.GetType("HollowMario.MapFallFlourish",true);var configure=flourish.GetMethod("Configure",S);object logger=Activator.CreateInstance(configure.GetParameters()[1].ParameterType,new object[]{"bench-checks"});configure.Invoke(null,new object[]{native,logger});
  var harmony=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.Harmony");object hooks=Activator.CreateInstance(harmony,new object[]{"bench-checks"});

  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}






  var arrivals=companion.GetType("HollowMario.ArrivalMario",true);var at=arrivals.GetMethod("At",S);var progress=arrivals.GetMethod("Progress",S);var kind=arrivals.GetMethod("Sequence",S);
  Check((int)kind.Invoke(null,new object[]{"Wake Up Ground",false})==1,"Ground wake uses full belly arrival");
  Check((int)kind.Invoke(null,new object[]{"Respawn Wake",false})==1,"Other arrival wake is covered");
  Check((int)kind.Invoke(null,new object[]{"Super Hard Land",false})==2,"Dreamgate impact uses belly landing");
  Check((int)kind.Invoke(null,new object[]{"Collect Normal 3",true})==3,"After dreamgate impact, get-up uses belly rise");
  Check((int)kind.Invoke(null,new object[]{"Collect Normal 3",false})==0,"Ordinary item pickups are not replaced");
  foreach(string other in new[]{"Land","HardLand","Prostrate Rise","Idle"})Check((int)kind.Invoke(null,new object[]{other,false})==0,"Other landing/recovery clips stay unchanged");
  Check((int)kind.Invoke(null,new object[]{"Hazard Respawn",false})==4,"Hazard recovery has its own animated arrival sequence");
  Type sample=at.ReturnType;var animation=sample.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sample.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  foreach(float first in new[]{0f,20f,32f}){
   Check((float)progress.Invoke(null,new object[]{first,first,45f})==0f,"Partial native clip begins new sequence at its start");
   Check((float)progress.Invoke(null,new object[]{45f,first,45f})==1f,"Partial native clip ends at original last frame");
   Check(Math.Abs((float)progress.Invoke(null,new object[]{(first+45f)*.5f,first,45f})-.5f)<.0001,"Speed follows actual remaining native timeline");
  }
  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var gameHero=engine.GetType("MarioHollow.MarioController").GetField("hero").FieldType;
  object recoveryHero=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(gameHero);
  var flagsField=gameHero.GetField("cState");object flags=Activator.CreateInstance(flagsField.FieldType);flagsField.SetValue(recoveryHero,flags);
  conversation.GetField("hero",S).SetValue(null,recoveryHero);
  conversation.GetField("actionPose",S).SetValue(null,true);conversation.GetField("actionGroup",S).SetValue(null,"hazard-return");
  foreach(bool right in new[]{false,true})foreach(float oldYaw in new[]{0f,(float)Math.PI*.25f,-(float)Math.PI*.25f}){
   flags.GetType().GetField("facingRight").SetValue(flags,right);conversation.GetField("actionYaw",S).SetValue(null,oldYaw);
   foreach(int visual in new[]{0x74}){
    conversation.GetField("animation",S).SetValue(null,visual);
    float yaw=(float)conversation.GetMethod("PoseFacing",S).Invoke(null,null);
    Check(Math.Abs(yaw-(right?1f:-1f)*(float)Math.PI*.5f)<.001f,"Hazard stumble follows gameplay facing instead of a stale camera angle");
   }
  }
  conversation.GetField("hero",S).SetValue(null,null);
  object realHero=conversation.GetField("heroType",S).GetValue(null);conversation.GetField("heroType",S).SetValue(null,typeof(FakeHero));
  conversation.GetField("actionPose",S).SetValue(null,true);conversation.GetField("actionGroup",S).SetValue(null,"wake");
  conversation.GetMethod("ObserveTransition",S).Invoke(null,new object[]{new object()});Check(FakeHero.Reads==0,"Room-entry observer cannot replace an owned arrival with walking/falling");
  conversation.GetField("actionPose",S).SetValue(null,false);conversation.GetField("actionGroup",S).SetValue(null,null);conversation.GetField("heroType",S).SetValue(null,realHero);
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var nativePlay=game.GetType("tk2dSpriteAnimator").GetMethod("Play",new[]{game.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});
  object info=harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{nativePlay});bool hookFound=false;foreach(object patch in (System.Collections.IEnumerable)info.GetType().GetField("Prefixes").GetValue(info)){var method=(MethodInfo)patch.GetType().GetProperty("PatchMethod").GetValue(patch,null);if(method.DeclaringType==arrivals)hookFound=true;}Check(hookFound,"Actual native animation entry is hooked");
  foreach(int mode in new[]{1,2,3})using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing((float)Math.PI*.5f);bool landed=false,rose=false;
   for(int i=0;i<=60;i++){
    float normalized=i/60f;object target=at.Invoke(null,new object[]{mode,normalized});int clip=(int)animation.GetValue(target),frame=(int)frameField.GetValue(target);
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
    try{
     if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);
     pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);
    }finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var state=pose.State.GetType();Check(Convert.ToInt32(state.GetField("animID").GetValue(pose.State))==clip,"Actual requested belly landing/getup animation");
    Check(Convert.ToInt32(state.GetField("animFrame").GetValue(pose.State))==frame,"Exact scrubbing to host timeline, including phase switch");
    Check(Math.Abs(Convert.ToSingle(state.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(state.GetField("py").GetValue(pose.State))+6000)<.01,"Visual landing never changes native player physics");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"ROM geometry throughout timed arrival");landed|=clip==0x2C;rose|=clip==0x5A;
   }
   Check(mode==1?landed&&rose:mode==2?landed&&!rose:rose&&!landed,"Arrival phases have expected coverage");
  }
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("Arrival clip selection, pickup exclusion, partial-start timing, native hook, exact ROM belly landing/get-up frames and stationary physics checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
