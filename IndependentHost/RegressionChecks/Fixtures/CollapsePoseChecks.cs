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






  var arrivals=companion.GetType("HollowMario.ArrivalMario",true);var at=arrivals.GetMethod("At",S);var next=arrivals.GetMethod("NextSequence",S);var moving=arrivals.GetMethod("MotionRemaining",S);
  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  Func<string,int,int> step=(name,previous)=>(int)next.Invoke(null,new object[]{name,previous});
  Check(step("DJ Get Land",0)==5&&step("Collect Normal 2",5)==12&&step("Collect Normal 3",12)==10,"Monarch/trap landing crouches, holds and stands up");
  Check(step("ToProne",0)==6&&step("Collect Normal 2",6)==7&&step("Collect Normal 3",7)==8,"Belly collapse advances into held half-rise then finishes standing");
  Check(step("Prostrate",6)==9&&step("Prostrate Rise",9)==3,"Flat prone hold and direct belly get-up covered");
  Check(step("GetUpToIdle",7)==11&&step("GetUpToIdle",12)==13&&step("GetUpToIdle",9)==14,"Return-to-idle variants select the correct starting posture");
  Check(step("Prostrate",0)==0&&step("Prostrate Rise",0)==0&&step("Collect Normal 3",0)==0,"Existing dream wake-up and unrelated item pickups are not claimed");
  Type sample=at.ReturnType;var animation=sample.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sample.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  object held=at.Invoke(null,new object[]{7,1f}),rise=at.Invoke(null,new object[]{8,0f});Check((int)animation.GetValue(held)==(int)animation.GetValue(rise)&&(int)frameField.GetValue(held)==(int)frameField.GetValue(rise),"Half-rise resumes from the exact held frame");
  foreach(int mode in new[]{5,6,7,8,9,10,11,12,13,14}){
   using(var pose=new OpeningMarioPose(native,library,0f,true)){
    pose.TalkFacing((float)Math.PI*.5f);
    for(int i=0;i<=30;i++){
     float progress=i/30f;object target=at.Invoke(null,new object[]{mode,progress});int clip=(int)animation.GetValue(target),frame=(int)frameField.GetValue(target);
     int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
     try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}
     finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
     var state=pose.State.GetType();Check(Convert.ToInt32(state.GetField("animID").GetValue(pose.State))==clip&&Convert.ToInt32(state.GetField("animFrame").GetValue(pose.State))==frame,"Exact requested native clip/frame throughout collapse/get-up");
     Check(Math.Abs(Convert.ToSingle(state.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(state.GetField("py").GetValue(pose.State))+6000)<.01,"Keyframing never moves player physics");
     Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"ROM geometry exists throughout each phase");
     if((mode==11||mode==13||mode==14)&&progress>=.4f)Check(clip==0xC5&&!(bool)moving.Invoke(null,new object[]{mode,progress}),"Looping idle cannot keep recovery ownership/control blocked");
    }
   }
   Check(!(bool)moving.Invoke(null,new object[]{mode,1f}),"Every new motion ends, including persistent holds");
  }
  var upgrades=companion.GetType("HollowMario.UpgradeKnockoutMario",true);var phase=upgrades.GetMethod("Phase",S);
  Check((int)phase.Invoke(null,new object[]{"Collect Magical 3b",false})==2,"Alternate Quake/Scream collapse selects back knockback");
  Check((int)phase.Invoke(null,new object[]{"Collect Normal 2",true})==5&&(int)phase.Invoke(null,new object[]{"Collect Normal 3",true})==6,"Upgrade holds on back, then wakes up");
  Check((int)phase.Invoke(null,new object[]{"Collect Normal 2",false})==0&&(int)phase.Invoke(null,new object[]{"GetUpToIdle",false})==0,"Unrelated pickups/returns are not treated as upgrade doubles");
  Check((int)phase.Invoke(null,new object[]{"GetUpToIdle",true})==7,"Alternate back get-up into idle is covered");
  object ending=upgrades.GetMethod("At",S).Invoke(null,new object[]{7,.5f,0f});Check((int)animation.GetValue(ending)==0xC5,"Back wake-up reaches neutral before idle looping begins");
  Console.WriteLine("PASS: landing/crouch/stand and belly-collapse/held-half-rise/get-up chains; ten timed phases with exact ROM frames and stationary physics; loop/hold release guards; alternate spell-upgrade back collapse and wake-up; unrelated dream/pickup exclusions.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("Arrival clip selection, pickup exclusion, partial-start timing, native hook, exact ROM belly landing/get-up frames and stationary physics checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
