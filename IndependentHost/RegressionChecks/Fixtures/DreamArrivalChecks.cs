// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class ArrivalPoseChecks {
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






  var arrival=companion.GetType("HollowMario.DreamArrivalMario",true);var at=arrival.GetMethod("At",S);var matches=arrival.GetMethod("Matches",S);
  Check((bool)matches.Invoke(null,new object[]{"Knight Dream Arrival","Warp In"}),"Dedicated dream actor is covered");
  Check(!(bool)matches.Invoke(null,new object[]{"Knight","Warp In"})&&!(bool)matches.Invoke(null,new object[]{"Knight Dream Arrival","DN Get 1"}),"Normal hero and unrelated cutscenes stay unchanged");
  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var play=game.GetType("tk2dSpriteAnimator").GetMethod("Play",new[]{game.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});
  object info=harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{play});bool hooked=false;foreach(object patch in (System.Collections.IEnumerable)info.GetType().GetField("Prefixes").GetValue(info)){var method=(MethodInfo)patch.GetType().GetProperty("PatchMethod").GetValue(patch,null);if(method.DeclaringType==arrival)hooked=true;}Check(hooked,"Native proxy animation starts the replacement");
  Type sample=at.ReturnType;var animation=sample.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sample.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var heightField=sample.GetField("Height",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  foreach(float t in new[]{0f,4f,7f,12f,14f,17f,20f,25f}){object target=at.Invoke(null,new object[]{t});int f=(int)frameField.GetValue(target),a=(int)animation.GetValue(target);if(t>=4f&&t<=7f)Check(a==0x13&&f==6,"Extended long-jump hold");if(t>=14f&&t<=20f)Check(a==0x5A&&f==22,"Half-rise is held during native crouch pause");if(t==25f)Check(a==0x5A&&f==37,"Rise ends standing before native control returns");}
  using(var pose=new OpeningMarioPose(native,library,0f,true)){
   float previousHeight=1f;
   for(int i=0;i<=100;i++){
    float t=i*.25f;object target=at.Invoke(null,new object[]{t});int a=(int)animation.GetValue(target),f=(int)frameField.GetValue(target);float height=(float)heightField.GetValue(target);
    Check(height<=previousHeight+.0001f&&height>=0f,"Visual descent is monotone and stops on the floor");previousHeight=height;
    int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{a});
    try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=a)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(f-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==a&&Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==f,"Actual ROM has exact requested frames through float/drop/get-up");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"ROM geometry present in every phase");
    Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Presentation never changes player physics");
   }
  }
  Console.WriteLine("PASS: dedicated dream proxy scope/hook; long-jump extension/hold, smooth visual drop, belly landing, held half-rise and full recovery; exact ROM frames and stationary player physics.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("Arrival clip selection, pickup exclusion, partial-start timing, native hook, exact ROM belly landing/get-up frames and stationary physics checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
