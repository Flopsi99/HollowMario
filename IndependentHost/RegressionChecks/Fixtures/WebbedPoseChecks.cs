// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class RecoveryTimingChecks {
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

  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var spells=companion.GetType("HollowMario.SpellMario",true);spells.GetMethod("Arm",S).Invoke(null,new object[]{hooks,native});
  conversation.GetField("requested",S).SetValue(null,true);var realHero=spells.GetField("heroType",S).GetValue(null);spells.GetField("heroType",S).SetValue(null,typeof(FakeHero));spells.GetMethod("BeforeState",S).Invoke(null,new object[]{new FakeState()});Check(FakeHero.Reads==0&&(bool)conversation.GetField("requested",S).GetValue(null),"Enemy/effect FSMs exit before reading the player or state details");spells.GetField("heroType",S).SetValue(null,realHero);
  realHero=conversation.GetField("heroType",S).GetValue(null);conversation.GetField("heroType",S).SetValue(null,typeof(FakeHero));conversation.GetMethod("BeforeNativeClip",S).Invoke(null,new object[]{null,new FakeClip()});Check(FakeHero.Reads==0&&(bool)conversation.GetField("requested",S).GetValue(null),"Unrelated animator clips exit before player inspection");conversation.GetField("heroType",S).SetValue(null,realHero);conversation.GetField("requested",S).SetValue(null,false);
  Check(conversation.GetField("ownerTarget",S).GetValue(null)!=null,"Interaction target lookup is cached when hooks are installed");
  Console.WriteLine("Native animation hooks, unrelated enemy/effect filtering and cached interaction lookup passed");
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}




  var web=companion.GetType("HollowMario.WebbedMario",true);var stage=web.GetMethod("Stage",S);var match=web.GetMethod("Matches",S);var at=web.GetMethod("At",S);
  Check((bool)match.Invoke(null,new object[]{"Fade","RestBench Spider"}),"Actual spider-capture controller matches");
  Check(!(bool)match.Invoke(null,new object[]{"Fade","Other Bench"})&&!(bool)match.Invoke(null,new object[]{"Bench Control Spider","RestBench Spider"}),"Unrelated fades and initial trap-bench scripts remain untouched");
  foreach(string name in new[]{"Capture End","Fade Up","Stuck","Struggle","Wiggle","Break"})Check((int)stage.Invoke(null,new object[]{name})==1,"Bound phase covered: "+name);
  Check((int)stage.Invoke(null,new object[]{"Fall"})==2&&(int)stage.Invoke(null,new object[]{"Land"})==3&&(int)stage.Invoke(null,new object[]{"Relinquish Control"})==0,"Native breakout stays covered through landing, then releases on control return");
  Check((int)stage.Invoke(null,new object[]{"Idle"})==-1,"Idle trap setup does not display trapped Mario");
  web.GetField("heroType",S).SetValue(null,typeof(FakeHero));web.GetMethod("BeforeState",S).Invoke(null,new object[]{new FakeState()});Check(FakeHero.Reads==0,"Unrelated FSMs exit without examining the player or state");
  conversation.GetField("requested",S).SetValue(null,true);conversation.GetField("actionPose",S).SetValue(null,true);conversation.GetField("actionGroup",S).SetValue(null,"web");conversation.GetField("actionHeld",S).SetValue(null,true);
  object[] sampleArgs={null};Check(!(bool)web.GetMethod("TrySample",S).Invoke(null,sampleArgs)&&!(bool)conversation.GetField("actionHeld",S).GetValue(null),"Destroyed/missing web actor cannot leave a stale input hold");
  conversation.GetField("requested",S).SetValue(null,false);conversation.GetField("actionPose",S).SetValue(null,false);conversation.GetField("actionGroup",S).SetValue(null,null);
  Type sample=at.ReturnType;var anim=sample.GetField("Animation",S|BindingFlags.Instance);var targetFrame=sample.GetField("Frame",S|BindingFlags.Instance);var seen=new System.Collections.Generic.HashSet<int>();
  using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing(0f);
   for(int tick=0;tick<90;tick++){
    object chosen=at.Invoke(null,new object[]{tick/30f});int clip=(int)anim.GetValue(chosen),frame=(int)targetFrame.GetValue(chosen);Check(clip==0x7A&&frame>=0&&frame<=5,"Shocked pose loops its six real frames");seen.Add(frame);
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
    try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}
    finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==clip&&Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==frame,"Actual ROM geometry samples exact shocked frames");
    Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Visual struggle stays suspended without moving player physics");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Mario mesh exists through repeated shock cycles");
   }
  }
  Check(seen.Count==6,"All six shock frames play repeatedly");
  Console.WriteLine("PASS: exact capture FSM scope/states, native release handover, missing-actor hold cleanup, six looping ROM shocked frames and stationary presentation physics.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();Console.WriteLine("Actual ROM stationary stumble/held recovery passed without opening a game or save.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
