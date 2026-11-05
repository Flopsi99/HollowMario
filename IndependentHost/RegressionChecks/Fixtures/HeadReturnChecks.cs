// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class HeadReturnChecks {
 class ActiveController {public bool Wanted=true;public int MarioId=7;}
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






  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var map=conversation.GetMethod("PoseForClip",S);var reuse=conversation.GetMethod("ContinueHeadReturn",S);
  var closing=conversation.GetMethod("ClosingFacing",S);
  foreach(bool right in new[]{false,true})foreach(float start in new[]{(float)Math.PI,(right?1f:-1f)*(float)Math.PI*.25f}){
   float first=(float)closing.Invoke(null,new object[]{start,right,0f});
   float end=(float)closing.Invoke(null,new object[]{start,right,1f});
   Check(Math.Abs(first-start)<.001f,"Closing starts at the existing tablet/NPC facing");
   Check(Math.Abs(Math.IEEERemainder(end-(right?1f:-1f)*Math.PI*.5f,2*Math.PI))<.001f,"Closing ends at gameplay facing without an extra side-dialogue stance");
   float middle=(float)closing.Invoke(null,new object[]{start,right,.5f});
   Check(Math.Abs(middle-(first+end)*.5f)<.001f,"Facing changes continuously during the native closing clip");
  }
  object rendererMarker=new object();conversation.GetField("renderer",S).SetValue(null,rendererMarker);
  foreach(string clip in new[]{"LookUpEnd","LookDownEnd"}){
   Check((int)map.Invoke(null,new object[]{clip})==0x1F,"Both head-return clips select Mario's return-to-neutral animation");
   conversation.GetField("requested",S).SetValue(null,true);conversation.GetField("returning",S).SetValue(null,false);conversation.GetField("actionPose",S).SetValue(null,false);conversation.GetField("transition",S).SetValue(null,false);
   conversation.GetField("oldHidden",S).SetValue(null,false);
   Check((bool)reuse.Invoke(null,new object[]{clip}),"Existing conversation owns the head-return transition");
   Check(ReferenceEquals(conversation.GetField("renderer",S).GetValue(null),rendererMarker)&&(bool)conversation.GetField("requested",S).GetValue(null),"Same presentation survives instead of stopping/rebuilding");
   Check((int)conversation.GetField("animation",S).GetValue(null)==0x1F,"Return pose applied to ongoing conversation");
   conversation.GetField("returning",S).SetValue(null,true);Check((bool)reuse.Invoke(null,new object[]{clip})&&!(bool)conversation.GetField("returning",S).GetValue(null),"Head-return tail reclaims existing presentation until the nod finishes, preventing Knight flashes");
  }
  conversation.GetField("renderer",S).SetValue(null,null);conversation.GetField("requested",S).SetValue(null,false);conversation.GetField("returning",S).SetValue(null,false);
  object originalHero=conversation.GetField("heroType",S).GetValue(null);conversation.GetField("heroType",S).SetValue(null,typeof(FakeHero));conversation.GetMethod("ObserveHeadReturn",S).Invoke(null,new object[]{new ActiveController()});Check(FakeHero.Reads==0,"Already-rendering gameplay Mario never reopens a stale hidden head animation");conversation.GetField("heroType",S).SetValue(null,originalHero);
  Type game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll")).GetType("tk2dSpriteAnimator");var actualPlay=game.GetMethod("Play",new[]{game.Assembly.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{actualPlay})!=null,"Native clip entry remains hooked");
  for(int cycle=0;cycle<2;cycle++)using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing(cycle==0?(float)Math.PI*.25f:-(float)Math.PI*.25f);
   bool sawReturnProgress=false;
   for(int frame=0;frame<90;frame++){
    int expected=frame<30?0x1E:frame<70?0x1F:0xC5;
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var state=pose.State.GetType();Check(Convert.ToInt32(state.GetField("animID").GetValue(pose.State))==expected,"Actual ROM look/return/idle sequence is used");
    if(expected==0x1F&&Convert.ToInt32(state.GetField("animFrame").GetValue(pose.State))>5)sawReturnProgress=true;
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Mario geometry remains available throughout head-return");
   }
   Check(sawReturnProgress,"Return animation advances normally");
  }
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("Both head-return clips, same-actor continuity, stale-animation guard, native clip hook and actual ROM look/return/idle geometry checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
