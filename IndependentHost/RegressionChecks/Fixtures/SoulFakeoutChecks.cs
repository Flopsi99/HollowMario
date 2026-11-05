// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class SoulFakeoutChecks {
 class ReturnFlags {public bool dead,hazardDeath,hazardRespawning,transitioning;}
 class ReturnHero {public static object instance;public bool controlReqlinquished,acceptingInput;public ReturnFlags cState=new ReturnFlags();}
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







  var upgrade=companion.GetType("HollowMario.UpgradeKnockoutMario",true);var fake=upgrade.GetMethod("IsSoulFakeout",S);var owns=upgrade.GetMethod("OwnsPlayback",S);var at=upgrade.GetMethod("FakeoutPose",S);
  Check((bool)fake.Invoke(null,new object[]{"Get Quake","Knight Get Quake Fake"})&&!(bool)fake.Invoke(null,new object[]{"Get Quake","Knight Get Quake"})&&!(bool)fake.Invoke(null,new object[]{"Get Fireball","Knight Get Quake Fake"}),"Only the fake Soul Master pickup uses this transition");
  Check(!(bool)owns.Invoke(null,new object[]{false,true,true,false}),"Disabled actor's looping animation cannot block gameplay");
  Check(!(bool)owns.Invoke(null,new object[]{true,true,true,true})&&(bool)owns.Invoke(null,new object[]{true,true,true,false}),"Fakeout follows native control; other active upgrade clips retain their timing");
  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));object info=harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HeroController").GetMethod("RegainControl")});bool installed=false;foreach(object patch in (System.Collections.IEnumerable)info.GetType().GetField("Postfixes").GetValue(info)){var method=(MethodInfo)patch.GetType().GetProperty("PatchMethod").GetValue(patch,null);if(method.DeclaringType==upgrade)installed=true;}Check(installed,"Authoritative native control-return hook installed");
  var oldHero=conversation.GetField("heroType",S).GetValue(null);var oldController=conversation.GetField("controllerType",S).GetValue(null);conversation.GetField("heroType",S).SetValue(null,typeof(ReturnHero));var hero=new ReturnHero{controlReqlinquished=true,acceptingInput=false};ReturnHero.instance=hero;
  conversation.GetField("requested",S).SetValue(null,true);conversation.GetField("actionPose",S).SetValue(null,true);conversation.GetField("actionGroup",S).SetValue(null,"upgrade");conversation.GetField("returning",S).SetValue(null,false);
  var returned=conversation.GetMethod("ActionControlReturned",S);returned.Invoke(null,new object[]{"upgrade",hero});Check(!(bool)conversation.GetField("returning",S).GetValue(null),"Cutscene control is never released early");
  hero.controlReqlinquished=false;hero.acceptingInput=true;hero.cState.hazardRespawning=true;returned.Invoke(null,new object[]{"upgrade",hero});Check(!(bool)conversation.GetField("returning",S).GetValue(null),"Hazard recovery cannot be bypassed");hero.cState.hazardRespawning=false;
  returned.Invoke(null,new object[]{"upgrade",new ReturnHero()});Check(!(bool)conversation.GetField("returning",S).GetValue(null),"Unrelated actor cannot finish the view");
  returned.Invoke(null,new object[]{"upgrade",hero});Check((bool)conversation.GetField("returning",S).GetValue(null)&&(bool)conversation.GetField("requested",S).GetValue(null),"Native regain releases controller but keeps last mesh until gameplay draws");
  object liveRenderer=new object();Controller.Instance=new Controller{Renderer=liveRenderer};conversation.GetField("controllerType",S).SetValue(null,typeof(Controller));conversation.GetMethod("AfterPlayableRender",S).Invoke(null,new object[]{new object()});Check((bool)conversation.GetField("requested",S).GetValue(null),"Unrelated renderer cannot remove the held view");conversation.GetMethod("AfterPlayableRender",S).Invoke(null,new object[]{liveRenderer});Check(!(bool)conversation.GetField("requested",S).GetValue(null)&&(bool)conversation.GetField("oldHidden",S).GetValue(null),"Playable draw clears stale dance and keeps Knight hidden");conversation.GetField("controllerType",S).SetValue(null,oldController);conversation.GetField("heroType",S).SetValue(null,oldHero);
  var sample=at.ReturnType;var anim=sample.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sample.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);float proneHeight=0f,standingHeight=0f;
  for(int cycle=0;cycle<2;cycle++)using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing((float)Math.PI*.5f);
   for(int tick=0;tick<60;tick++){
    float age=tick/30f,land=tick>=15?(tick-15)/30f:0f;object target=at.Invoke(null,new object[]{age,land,tick>=15,false});int a=(int)anim.GetValue(target),frame=(int)frameField.GetValue(target);
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{a});try{if(frame>=0){if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=a)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));}b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==a&&(frame<0||Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==frame),"Real ROM reaches every requested fall/quick get-up frame");Check(a!=0xCD,"Fakeout never remains in star dance");Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Native boss script keeps player movement");
    if(tick==17||tick==40){int triangles=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));float[] vertices=new float[triangles*9];Marshal.Copy((IntPtr)bt.GetField("position").GetValue(b),vertices,0,vertices.Length);float low=float.PositiveInfinity,high=float.NegativeInfinity;for(int v=1;v<vertices.Length;v+=3){low=Math.Min(low,vertices[v]);high=Math.Max(high,vertices[v]);}if(tick==17)proneHeight=high-low;else standingHeight=high-low;}
   }
  }
  Check(proneHeight<standingHeight*.65f,"Pose actually falls low and then stands for phase two");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("PASS: Soul Master fakeout scope, inactive-loop release, authoritative control return, retained-mesh handover, real ROM backward fall/quick rise and stationary player physics.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
