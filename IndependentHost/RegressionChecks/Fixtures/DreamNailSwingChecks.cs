// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class DreamNailSwingChecks {
 class Clip {public string name;public object[] frames;}
 class Clock {public Clip CurrentClip;public float clipTime;}
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







  var dream=companion.GetType("HollowMario.DreamNailMario",true);var at=dream.GetMethod("At",S);var phase=dream.GetMethod("Phase",S);var sampleType=at.ReturnType;
  var yawField=sampleType.GetField("Yaw",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var animationField=sampleType.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sampleType.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  foreach(bool right in new[]{true,false}){
   float forward=right?(float)Math.PI*.5f:-(float)Math.PI*.5f;
   object start=at.Invoke(null,new object[]{1,0f,right}),hold=at.Invoke(null,new object[]{2,.8f,right});
   Check(Math.Abs((float)yawField.GetValue(start)-forward)<.001f,"Grab begins facing attack direction");Check(Math.Abs((float)yawField.GetValue(hold)+forward)<.001f,"Charged grip faces behind attack direction");
   float previous=-forward;for(int i=0;i<=100;i++){object sample=at.Invoke(null,new object[]{3,i/100f,right});float yaw=(float)yawField.GetValue(sample);Check(right?yaw>=previous-.0001f:yaw<=previous+.0001f,"Mirrored half-turn is monotonic");if(i>=40)Check(Math.Abs(yaw-forward)<.001f,"Swing stops facing forward after brief strike window");previous=yaw;}
   Check(Math.Abs(previous-(-forward))- (float)Math.PI <.001f,"Exactly half-turn, no extra revolution");
  }
  foreach(string state in new[]{"Set Charge Start","Warp Charge Start","Warp Charge","Warp Effect","Inactive","Regain Control","Idle Anim"})Check((int)phase.Invoke(null,new object[]{state})==0,"Dreamgate and idle states clear nail-specific animation");
  var spells=companion.GetType("HollowMario.SpellMario",true);object warp=spells.GetMethod("ForOtherState",S).Invoke(null,new object[]{"Dream Nail","Warp Charge"});Check((int)warp.GetType().GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(warp)==0x94,"Dreamgate twirl remains intact");
  var clock=new Clock{CurrentClip=new Clip{name="DN Slash",frames=new object[10]},clipTime=0f};dream.GetField("animator",S).SetValue(null,clock);dream.GetField("phase",S).SetValue(null,3);dream.GetField("right",S).SetValue(null,true);
  object[] sampled={null};Check((bool)dream.GetMethod("TrySample",S).Invoke(null,sampled),"Reads actual animation clock");Check((float)yawField.GetValue(sampled[0])<0f,"Slash clock starts behind");clock.clipTime=4f;sampled=new object[]{null};dream.GetMethod("TrySample",S).Invoke(null,sampled);Check((float)yawField.GetValue(sampled[0])>1.5f,"Advanced native frames reach forward without a separate timer");
  clock.CurrentClip.name="DN Cancel";clock.CurrentClip.frames=new object[3];clock.clipTime=1f;dream.GetField("phase",S).SetValue(null,4);dream.GetField("cancelFrom",S).SetValue(null,0f);sampled=new object[]{null};dream.GetMethod("TrySample",S).Invoke(null,sampled);Check((float)yawField.GetValue(sampled[0])>0f&&(float)yawField.GetValue(sampled[0])<1.5f,"Early cancellation unwinds from actual partial turn");dream.GetMethod("Forget",S).Invoke(null,null);
  var seen=new System.Collections.Generic.HashSet<int>();
  foreach(bool right in new[]{true,false})using(var pose=new OpeningMarioPose(native,library,0f,true)){
   for(int t=0;t<140;t++){
    int stage=t<30?1:t<60?2:t<110?3:4;float p=stage==1?t/29f:stage==2?0f:stage==3?(t-60)/49f:(t-110)/29f;
    object target=at.Invoke(null,new object[]{stage,p,right});int clip=(int)animationField.GetValue(target),frame=(int)frameField.GetValue(target);float angle=(float)yawField.GetValue(target);pose.TalkFacing(angle);
    int old=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{old});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==clip&&Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==frame,"Real ROM Bowser grip/swing/release frames");
    Check(Math.Abs(Convert.ToSingle(st.GetField("faceAngle").GetValue(pose.State))-angle)<.001f,"ROM yaw follows mirrored keyframes");Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"No Bowser-grab physics or movement leaks into gameplay");Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Mesh remains rendered through every pose");seen.Add(clip);
   }
  }
  Check(seen.Count==4,"All four Bowser-tail poses exercised");flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Actual ROM grab/hold/swing/release poses, mirrored 180-degree turn, held charge, native-clock timing, cancellation, Dreamgate exclusion and stationary actor checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
