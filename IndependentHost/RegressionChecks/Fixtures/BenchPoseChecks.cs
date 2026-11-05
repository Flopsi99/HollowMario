// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
class BenchPoseChecks {
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
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");
  Console.WriteLine("Hooks armed");var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}
  for(int cycle=0;cycle<2;cycle++){
   bench.GetField("animation",S).SetValue(null,0x84);bench.GetField("animationTicks",S).SetValue(null,0);bench.GetField("previousFrame",S).SetValue(null,-1);
   Console.WriteLine("Creating bench actor "+cycle);using(var pose=new OpeningMarioPose(native,library,0f,true)){
    bool slept=false;int sleepFirst=-1,sleepLast=-1;
    for(int i=0;i<240;i++){TestEnvironment.PullAudio(audioArgs);if(i%60==0)Console.WriteLine("Bench tick "+i);
     int anim=(int)bench.GetField("animation",S).GetValue(null);int before=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{anim});
     try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{before});}
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
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var p in pins)p.Free();
  Console.WriteLine("Bench hooks, normal/respawn entry scope, sit-to-sleep, sustained seated animation, camera facing, wake and repeat visits with production parking passed (no game or save opened).");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
