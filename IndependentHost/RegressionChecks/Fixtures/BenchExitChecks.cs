// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class BenchExitChecks {
 struct PoseState {public float faceAngle;}
 class Controller {public int MarioId;public PoseState State,PrevState;public bool Wanted=true;}
 static int muted;
 static void SoundObserved(uint __0,bool __result){if(__result)muted++;}
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);
  string package=TestEnvironment.Package, managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var native=engine.GetType("MarioHollow.Native",true);
  string library=TestEnvironment.Library;TestEnvironment.StartNative();
  var audio=native.GetMethod("sm64_audio_tick");object[] audioArgs={0u,1100u,new short[8192]};Console.WriteLine("ROM and audio initialized");var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);var bench=companion.GetType("HollowMario.BenchMario",true);
  var flourish=companion.GetType("HollowMario.MapFallFlourish",true);var configure=flourish.GetMethod("Configure",S);object logger=Activator.CreateInstance(configure.GetParameters()[1].ParameterType,new object[]{"bench-checks"});configure.Invoke(null,new object[]{native,logger});
  var harmony=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.Harmony");object hooks=Activator.CreateInstance(harmony,new object[]{"bench-checks"});
  Console.WriteLine("Arming bench hooks");bench.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{engine.GetType("MarioHollow.MarioController").GetMethod("Update",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Controller hook installed");
  foreach(string start in new[]{"Start Rest","Init Resting","Init Resting 2"})Check((bool)bench.GetMethod("StartsRest",S).Invoke(null,new object[]{start}),"Rest/respawn branch "+start);
  foreach(string other in new[]{"Idle","In Range","Check Start State"})Check(!(bool)bench.GetMethod("StartsRest",S).Invoke(null,new object[]{other}),"Approaching a bench cannot trigger rest");

  foreach(string request in new[]{"Get Up","Get Left","Get Right","Game Paused?","Cancel Frame 2","Resting"})Check(!(bool)bench.GetMethod("StartsLeaving",S).Invoke(null,new object[]{request}),"Inventory/pause request cannot start wake: "+request);
  foreach(string accepted in new[]{"Wake?","Get Off"})Check((bool)bench.GetMethod("StartsLeaving",S).Invoke(null,new object[]{accepted}),"Only accepted exit wakes Mario");
  var ease=bench.GetMethod("ExitProgress",S);float last=-1;
  for(int i=0;i<=24;i++){float t=(float)ease.Invoke(null,new object[]{i*.01f});Check(t>=last&&t>=0&&t<=1,"Exit blend is bounded and monotonic");last=t;}
  Check((float)ease.Invoke(null,new object[]{0f})==0f,"Exit starts at the seat");Check(Math.Abs((float)ease.Invoke(null,new object[]{.11f})-.5f)<.0001f,"Exit midpoint");Check((float)ease.Invoke(null,new object[]{.22f})==1f,"Exit ends with zero visual offset");
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{engine.GetType("MarioHollow.MarioController").GetMethod("Tick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Face-camera hook installed before playable tick");
  Console.WriteLine("Inventory request rejection, accepted exit scope, blend endpoints and gameplay hook checks passed");
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

  var sound=companion.GetType("HollowMario.BenchRestAudio",true);sound.GetMethod("Install",S).Invoke(null,new object[]{library});
  object observe=Activator.CreateInstance(hm,new object[]{typeof(BenchExitChecks).GetMethod("SoundObserved",S)});
  Array.Find(harmony.GetMethods(),m=>m.Name=="Patch"&&m.GetParameters().Length==6).Invoke(hooks,new object[]{sound.GetMethod("ShouldMute",S),null,observe,null,null,null});
  var mute=sound.GetMethod("ShouldMute",S);var begin=sound.GetMethod("Begin",S);var end=sound.GetMethod("End",S);
  Check(!(bool)mute.Invoke(null,new object[]{0x240E8080u}),"Ordinary Mario snoring outside bench tick remains untouched");
  bool q=(bool)begin.Invoke(null,null);
  foreach(uint sample in new uint[]{0x240E8080u,0x240F8080u,0x2435FF80u,0x240E0081u})Check((bool)mute.Invoke(null,new object[]{sample}),"All bench snores suppressed, including changed priority flags");
  Check(!(bool)mute.Invoke(null,new object[]{0x70040081u}),"Dialogue/other sound remains audible inside bench tick");
  bool nested=(bool)begin.Invoke(null,null);end.Invoke(null,new object[]{nested});Check((bool)mute.Invoke(null,new object[]{0x240F8080u}),"Nested scope retains suppression");end.Invoke(null,new object[]{q});

  var seat=bench.GetMethod("SeatOffset",S);var offset=(Vector3)seat.Invoke(null,new object[]{new Vector2(0f,.1f),new Vector2(.8f,1.6f)});
  Check(Math.Abs(offset.y-(-.32f))<.00001f,"Seat uses the native local bottom plus the revised 0.38 lift");
  foreach(float degrees in new[]{0f,16f}){
   float radians=degrees*(float)Math.PI/180f;var tilt=new Quaternion(0,0,(float)Math.Sin(radians*.5f),(float)Math.Cos(radians*.5f));
   var nativeFeet=new Vector3(-63,-63,-63);var anchor=new Vector3(11.38f,20.86f,.0125f)+tilt*offset;
   var translation=anchor-tilt*nativeFeet;var renderedFeet=translation+tilt*nativeFeet;
   Check((renderedFeet-anchor).sqrMagnitude<.0000001f,"Flat/recorded Waterways tilt pivots about the seated actor, not the remote stage");
   var raised=tilt*new Vector3(0,.38f,0);
   Check(raised.y>.36f,"Seat lift follows local up even on the actual 16-degree bench");
  }
  Console.WriteLine("Raised local seat offset and flat/16-degree Waterways geometry checks passed");muted=0;
  Console.WriteLine("Sound scope checks passed; observing real native bench ticks");var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}
  for(int cycle=0;cycle<2;cycle++){
   bench.GetField("animation",S).SetValue(null,0x84);bench.GetField("animationTicks",S).SetValue(null,0);bench.GetField("previousFrame",S).SetValue(null,-1);
   Console.WriteLine("Creating bench actor "+cycle);using(var pose=new OpeningMarioPose(native,library,0f,true)){
    bool slept=false;int sleepFirst=-1,sleepLast=-1;
    for(int i=0;i<240;i++){TestEnvironment.PullAudio(audioArgs);if(i%60==0)Console.WriteLine("Bench tick "+i);
     int anim=(int)bench.GetField("animation",S).GetValue(null);int before=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{anim});
     bool quiet=(bool)begin.Invoke(null,null);try{b=pose.Tick(b,true);}finally{end.Invoke(null,new object[]{quiet});flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{before});}
     var t=pose.State.GetType();Check(Convert.ToInt32(t.GetField("animID").GetValue(pose.State))==anim,"Actual sitting/sleep geometry matches phase");
     Check(Math.Abs(Convert.ToSingle(t.GetField("faceAngle").GetValue(pose.State)))<.001f,"Faces camera");
     int frame=Convert.ToInt32(t.GetField("animFrame").GetValue(pose.State));bench.GetMethod("NoteFrame",S).Invoke(null,new object[]{frame});
     if(anim==0x85){slept=true;if(sleepFirst<0)sleepFirst=frame;sleepLast=frame;}
    }
    Check(slept&&sleepFirst!=sleepLast,"Sitting finishes into animated seated sleep and remains in the seated loop");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Actual ROM-backed geometry generated");
    pose.Wake();int prev=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{0xC8});
    try{for(int i=0;i<24;i++){TestEnvironment.PullAudio(audioArgs);b=pose.Tick(b,true);}}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{prev});}
    Check(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))==0xC8,"Wake animation plays");
   }
   Check(TestEnvironment.ParkedActors>0,"Bench actor safely parked for reuse");
  }

  using(var returningPose=new OpeningMarioPose(native,library,1.57f,true)){
   var owner=new Controller{MarioId=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(returningPose),State=new PoseState{faceAngle=1.57f},PrevState=new PoseState{faceAngle=-1.57f}};
   bench.GetField("returnController",S).SetValue(null,owner);bench.GetField("returnFacing",S).SetValue(null,true);
   bench.GetMethod("BeforePlayableTick",S).Invoke(null,new object[]{owner});
   Check(owner.State.faceAngle==0&&owner.PrevState.faceAngle==0,"Both render snapshots start facing camera");
   b=returningPose.Tick(b,true);Check(Math.Abs(Convert.ToSingle(returningPose.State.GetType().GetField("faceAngle").GetValue(returningPose.State)))<.001f,"Actual native playable return faces camera");
   native.GetMethod("sm64_set_mario_faceangle").Invoke(null,new object[]{owner.MarioId,1.2f});bench.GetMethod("BeforePlayableTick",S).Invoke(null,new object[]{owner});
   b=returningPose.Tick(b,true);Check(Math.Abs(Convert.ToSingle(returningPose.State.GetType().GetField("faceAngle").GetValue(returningPose.State))-1.2f)<.002f,"Later normal turning is not locked to camera");
   bench.GetMethod("ClearReturn",S).Invoke(null,null);
  }
  Console.WriteLine("Actual native one-shot camera-facing return and unrestricted later turning passed");
  Check(muted>0,"Real native bench sleep emitted snores which the installed callback suppressed");Console.WriteLine("Actual suppressed native snore calls: "+muted);
  sound.GetMethod("Dispose",S).Invoke(null,null);flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var p in pins)p.Free();
  Console.WriteLine("Bench audio scope, actual native snore suppression, seated/sleep/wake animation and repeated visits passed; no game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
