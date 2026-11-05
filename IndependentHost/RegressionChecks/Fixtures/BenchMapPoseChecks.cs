// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class BenchMapPoseChecks {
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




  var mapState=bench.GetMethod("MapReadingState",S);int mapClip=(int)bench.GetField("SeatedMap",S).GetRawConstantValue();
  foreach(string state in new[]{"Open Map","Map Idle"})Check((int)mapState.Invoke(null,new object[]{state})==1,"Native bench map states select the sitting carry pose");
  foreach(string state in new[]{"Close Map","Close Anim","Inv Map Open","Resting","Sit Idle","Wake?","Get Off"})Check((int)mapState.Invoke(null,new object[]{state})==0,"Closing or accepted exit releases the map pose");
  foreach(string state in new[]{"Get Up","Get Left","Get Right","Game Paused?"})Check((int)mapState.Invoke(null,new object[]{state})==-1,"Unaccepted/menu input requests do not alter the seated pose");
  Check(mapClip==0x45,"Selected ROM animation is sliding on bottom while holding an item");
  using(var mapPose=new OpeningMarioPose(native,library,0f,true)){
   for(int cycle=0;cycle<3;cycle++){
    mapPose.TalkFacing(0f);mapPose.SetAnimationFrame(-1);
    for(int frame=0;frame<75;frame++){
     int expected=frame<50?mapClip:0x85;if(frame==50)mapPose.TalkFacing(0f,true);
     int old=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});try{b=mapPose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{old});}TestEnvironment.PullAudio(audioArgs);
     var st=mapPose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(mapPose.State))==expected,"Actual ROM sitting-carry pose switches back to seated sleep");Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(mapPose.State))+6000f)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(mapPose.State))+6000f)<.01,"Sitting-carry animation never activates sliding movement");Check(Math.Abs(Convert.ToSingle(st.GetField("faceAngle").GetValue(mapPose.State)))<.002,"Bench camera-facing direction remains unchanged");Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Seated map and ordinary rest have ROM geometry");
    }
   }
  }
  Console.WriteLine("Bench map open/close and inventory request filtering, actual ROM sitting-carry/rest transitions, stationary position and camera-facing checks passed.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();Console.WriteLine("Bench seated-map checks passed without opening a game or save.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
