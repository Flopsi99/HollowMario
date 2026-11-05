// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class UnnCrawlChecks {
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





  var presentation=companion.GetType("HollowMario.ConversationMario",true);var pace=presentation.GetMethod("WalkRate",S);
  foreach(float speed in new[]{0f,1.5f,-1.5f,6f})Check((float)pace.Invoke(null,new object[]{0x99,speed})==3f,"Unn crawl uses three-times animation pace in both directions");
  Check((float)pace.Invoke(null,new object[]{0x97,0f})==1f,"Ordinary healing pace is unchanged");
  var spells=companion.GetType("HollowMario.SpellMario",true);
  var select=spells.GetMethod("ForFocusState",S);Type plan=select.ReturnType;
  foreach(string state in new[]{"Start Slug Anim","Focus Start","Focus","Focus Heal","Focus S","Focus Left","Focus Right","Focus Heal 2"}){
   object normal=select.Invoke(null,new object[]{state,false}),slug=select.Invoke(null,new object[]{state,true});
   Check((int)plan.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(normal)==0x97,"Ordinary healing remains crouching");
   Check((int)plan.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(slug)==0x99,"Shape of Unn healing uses crawling");
   Check((bool)plan.GetField("HoldControl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(slug),"Native healing keeps ownership");
  }
  foreach(string state in new[]{"Focus Cancel","Focus Cancel 2","Focus Get Finish","Focus Get Finish 2"}){
   object p=select.Invoke(null,new object[]{state,true});Check((int)plan.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x96,"Cancel/completion still stands up");
  }
  var movement=spells.GetMethod("CrawlSpeed",S);
  Check(Math.Abs((float)movement.Invoke(null,new object[]{10f,10.05f,1f/30f})-1.5f)<.001f,"Scripted transform movement animates even when rigidbody velocity is zero");
  Check((float)movement.Invoke(null,new object[]{10f,10f,1f/30f})==0f,"Stationary focus holds still");
  Check((float)movement.Invoke(null,new object[]{10f,9f,0f})==0f,"Paused focus cannot divide by zero");
  var hold=spells.GetMethod("CrawlHold",S);int movementFrames=0;int last=-1;
  using(var pose=new OpeningMarioPose(native,library,0f,true)){
   for(int frame=0;frame<160;frame++){
    float speed=frame<40?0:frame<90?1.5f:frame<130?-1.5f:0;
    float yaw=speed<0?-(float)Math.PI*.5f:(float)Math.PI*.5f;
    pose.TalkFacing(yaw);int held=(int)hold.Invoke(null,new object[]{speed});pose.LimitAnimationFrame(0x99,held);
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{0x99});try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();int af=Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State));
    Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==0x99,"Actual ROM crawl selected");
    if(speed==0)Check(af==0,"Idle Unn holds a stationary crawling pose");else if(af!=last)movementFrames++;
    Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Presentation never applies independent crawling physics");
    Check(Math.Abs(Convert.ToSingle(st.GetField("faceAngle").GetValue(pose.State))-yaw)<.002,"Crawl faces either direction");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"ROM crawl geometry present");last=af;
   }
  }
  Check(movementFrames>60,"Moving healing runs crawling cycle");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Unn/ordinary healing selection, cancellation, actual ROM crawling/idle poses, direction changes and stationary presentation physics passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
