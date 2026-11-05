// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class KnockoutFrameCheck {
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






 var conversation=companion.GetType("HollowMario.ConversationMario",true);var choose=conversation.GetMethod("TransitionAction",S);var facing=conversation.GetMethod("TransitionHurtFacing",S);
 foreach(int normal in new[]{0x48,0xC5,0x4D,0x56}){
  Check((int)choose.Invoke(null,new object[]{normal,true,false})==2&&(int)choose.Invoke(null,new object[]{normal,true,true})==2,"Hurt overrides every ordinary transition and stale crystal-dash flag");
  Check((int)choose.Invoke(null,new object[]{normal,false,false})==normal&&(int)choose.Invoke(null,new object[]{normal,false,true})==0x8C,"Normal and crystal transitions retain their own poses after recoil");
 }
 Check((float)facing.Invoke(null,new object[]{3f,true})<0f&&(float)facing.Invoke(null,new object[]{-3f,false})>0f,"Backward recoil faces against horizontal travel");
 Check((float)facing.Invoke(null,new object[]{0f,true})>0f&&(float)facing.Invoke(null,new object[]{0f,false})<0f,"Vertical/stationary recoil retains the native facing");
 var recoilFrames=new System.Collections.Generic.HashSet<int>();
 using(var pose=new OpeningMarioPose(native,library,0f,true)){
  pose.TalkFacing(1.5707963f);
  for(int n=0;n<90;n++){
   int old=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{2});
   try{if(n<15)pose.LimitAnimationFrame(2,0);b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{old});}TestEnvironment.PullAudio(audioArgs);
   var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==2,"Actual ROM uses backward airborne knockback");
   if(n>=1&&n<15)Check(Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==0,"Hit-stop pose at tick "+n+": actual frame="+Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State)));
   if(n>=15)recoilFrames.Add(Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State)));
   Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Animation replacement never changes transition physics");
  }
 }
 Check(recoilFrames.Count>0,"Native airborne knockback remains available throughout the transition");
 Console.WriteLine("PASS: hurt-transition priority, normal/crystal fallback, recoil facing, actual ROM backward knockback/hit-stop frames and stationary presentation physics. No game launched.");
 return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
