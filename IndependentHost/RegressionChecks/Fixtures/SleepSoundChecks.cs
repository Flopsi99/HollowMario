// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class SleepSoundChecks {
 class FakeHero {public static int Reads; public static object instance {get{Reads++;throw new Exception("Player should not be inspected");}}}
 class FakeFsm {public string Name="Enemy Control";}
 class FakeState {public FakeFsm Fsm=new FakeFsm();public string Name {get{throw new Exception("Enemy state details should not be inspected");}}}
 class FakeClip {public string name="Run";}

 class MapController {public static MapController Instance;public object hero;public int MarioId=-1;}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Volume(float volume);
 class Controller {public static Controller Instance;public object Renderer;}
 [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Sound(uint bits,IntPtr source);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Process();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Bank(byte bank,out byte playing,out byte count,out byte sample);
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






  var quiet=companion.GetType("HollowMario.BenchRestAudio",true);quiet.GetMethod("Install",S).Invoke(null,new object[]{library});
  var symbols=(System.Collections.Generic.Dictionary<string,IntPtr>)companion.GetType("HollowMario.NativeSymbols").GetMethod("Functions",S).Invoke(null,new object[]{library,GetModuleHandle(library),new[]{"play_sound","process_all_sound_requests","get_currently_playing_sound"}});
  var play=(Sound)Marshal.GetDelegateForFunctionPointer(symbols["play_sound"],typeof(Sound));var process=(Process)Marshal.GetDelegateForFunctionPointer(symbols["process_all_sound_requests"],typeof(Process));var query=(Bank)Marshal.GetDelegateForFunctionPointer(symbols["get_currently_playing_sound"],typeof(Bank));
  var pin=GCHandle.Alloc(new float[3],GCHandleType.Pinned);IntPtr source=pin.AddrOfPinnedObject();byte playing,count,sample;
  var stop=quiet.GetMethod("StopSleeping",S);var sources=quiet.GetField("sleepingSources",S).GetValue(null);Func<int> tracked=()=> (int)sources.GetType().GetProperty("Count").GetValue(sources,null);
  foreach(uint snore in new[]{0x240E8080u,0x240F8080u,0x2435FF80u}){
   foreach(bool alreadyProcessed in new[]{false,true}){
    play(snore,source);play(0x700E0081u,source);if(alreadyProcessed)process();
    Check(tracked()==1,"Normal snore records source without muting it");
    stop.Invoke(null,null);Check(tracked()==0,"Forced wake clears tracked snore");
    for(int step=0;step<8;step++)TestEnvironment.PullAudio(audioArgs);
    query(2,out playing,out count,out sample);Check(count==0,"Actual voice bank no longer contains queued/playing snore");
    query(7,out playing,out count,out sample);Check(count>0,"Other sound bank remains present");
   }
  }
  bool previousQuiet=(bool)quiet.GetMethod("Begin",S).Invoke(null,null);play(0x2435FF80u,source);quiet.GetMethod("End",S).Invoke(null,new object[]{previousQuiet});Check(tracked()==0,"Quiet bench pose cannot start a new snore");
  using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing(0,true);for(int frame=0;frame<80;frame++){b=pose.Tick(b,true);TestEnvironment.PullAudio(audioArgs);}
   Check(tracked()>0,"Actual sleeping Mario produces a tracked snore");
  }
  Check(tracked()==0,"Parking the actual actor for forced handover clears snores");
  for(int step=0;step<8;step++)TestEnvironment.PullAudio(audioArgs);query(2,out playing,out count,out sample);Check(count==0,"No sleeping voice remains after actor handover");
  using(var pose=new OpeningMarioPose(native,library,0f,true)){
   b=pose.Tick(b,true);
   int actorId=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(pose);
   native.GetMethod("sm64_set_mario_action").Invoke(null,new object[]{actorId,0x0C400202u});
   bool slept=false;
   for(int frame=0;frame<2800;frame++){
    b=pose.Tick(b,true);TestEnvironment.PullAudio(audioArgs);
    uint state=Convert.ToUInt32(pose.State.GetType().GetField("action").GetValue(pose.State));
    slept|=state==0x0C000203u;
    if(frame%100==0){GC.Collect();GC.WaitForPendingFinalizers();}
   }
   Check(slept,"Actual start-sleeping transition reaches sustained native sleep with audio and garbage collections");
   pose.Wake();for(int frame=0;frame<50;frame++){b=pose.Tick(b,true);TestEnvironment.PullAudio(audioArgs);}
  }
  pin.Free();quiet.GetMethod("Dispose",S).Invoke(null,null);flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("All three queued/playing snores stop at forced handover; normal sleep, quiet bench suppression and unrelated sound preservation passed using the native audio engine.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
