// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class RecoveryTimingChecks {
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




  var death=companion.GetType("HollowMario.DeathMario",true);death.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native});
  var cap=companion.GetType("HollowMario.DeathCap",true);var match=cap.GetMethod("IsHeadSpawn",S);var read=cap.GetMethod("Read",S);var extract=cap.GetMethod("Extract",S);
  Check((bool)match.Invoke(null,new object[]{"Hero Death Anim","Head Left","Corpse Head",true})&&(bool)match.Invoke(null,new object[]{"Hero Death Anim","Head Right","Corpse Head",true}),"Both actual corpse-head spawn steps match");
  foreach(object[] wrong in new[]{new object[]{"Other","Head Left","Corpse Head",true},new object[]{"Hero Death Anim","Blow","Corpse Head",true},new object[]{"Hero Death Anim","Head Left","Corpse Nail Hero",true},new object[]{"Hero Death Anim","Head Left","Corpse Head",false}})Check(!(bool)match.Invoke(null,wrong),"Other props, deaths and states remain untouched");
  Type gt=read.ReturnType;var countField=gt.GetField("Count",S|BindingFlags.Instance);var capColors=gt.GetField("Color",S|BindingFlags.Instance);var capUv=gt.GetField("Uv",S|BindingFlags.Instance);var capPositions=gt.GetField("Position",S|BindingFlags.Instance);
  foreach(int animation in new[]{0x02,0x03})foreach(short frame in new short[]{0,5,15})using(var pose=new OpeningMarioPose(native,library,0f,true)){
   pose.TalkFacing(0f);
   for(int warm=0;warm<2;warm++){
    int prev=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{animation});try{pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{prev});}TestEnvironment.PullAudio(audioArgs);
   }
   Check((Convert.ToUInt32(pose.State.GetType().GetField("flags").GetValue(pose.State))&16u)!=0,"New/reused actor starts with his cap");
   object wearing=read.Invoke(null,new[]{b});int wearingCount=(int)countField.GetValue(wearing);
   Check(pose.SetCapVisible(false),"Native hatless switch is available");
   int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{animation});try{pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
   object bare=read.Invoke(null,new[]{b});object result=extract.Invoke(null,new[]{wearing,bare});int triangles=(int)countField.GetValue(result);var colors=(float[])capColors.GetValue(result);var uv=(float[])capUv.GetValue(result);var positions=(float[])capPositions.GetValue(result);
   Check(triangles>=12&&triangles<=80,"Separated cap contains a compact model, not Mario's body or hair");
   bool textured=false;for(int i=0;i<triangles;i++){Check(colors[i*9]>.9f&&colors[i*9+1]<.08f&&colors[i*9+2]<.08f,"No skin, hair or gloves in cap mesh");for(int j=0;j<6;j++)textured|=uv[i*6+j]!=1f;}
   var visible=engine.GetType("MarioHollow.MeshProjection",true).GetMethod("Visible");int rear=0;
   for(int degrees=-180;degrees<=180;degrees+=45)for(int t=0;t<triangles;t++){
    double angle=degrees*Math.PI/180;float co=(float)Math.Cos(angle),si=(float)Math.Sin(angle);int at=t*9;
    Func<int,Vector3> rotated=i=>new Vector3(positions[i],positions[i+1]*co+positions[i+2]*si,positions[i+1]*si-positions[i+2]*co);
    Vector3 a=rotated(at),c=rotated(at+6),d=rotated(at+3);
    Check((bool)visible.Invoke(null,new object[]{c-a,d-a,true}),"Detached ROM cap retains its interior-facing surfaces through rotation");
    if(!(bool)visible.Invoke(null,new object[]{c-a,d-a,false}))rear++;
   }
   Check(rear>0,"Actual cap includes reverse-facing surfaces which body culling would remove");
   Check(textured,"ROM cap emblem texture coordinates retained");foreach(float p in positions)Check(!float.IsNaN(p)&&Math.Abs(p)<100,"Cap vertices centered without remote-stage coordinates");
   Check((Convert.ToUInt32(pose.State.GetType().GetField("flags").GetValue(pose.State))&16u)==0,"Dying Mario is hatless after separation");
   Check(pose.SetCapVisible(true),"Cap can be restored without changing upgrades");
   previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{animation});try{pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
   Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))==wearingCount,"Original cap-on mesh restored");
   Check(pose.SetCapVisible(false),"Leave pose hatless to check disposal restores parked actor");
   Console.WriteLine("Death pose "+animation+" frame "+frame+": separated "+triangles+" cap triangles.");
  }
  using(var fresh=new OpeningMarioPose(native,library,0f,true)){fresh.TalkFacing(0f);b=fresh.Tick(b,true);TestEnvironment.PullAudio(audioArgs);Check((Convert.ToUInt32(fresh.State.GetType().GetField("flags").GetValue(fresh.State))&16u)!=0,"Respawn/reused Mario gets his cap back automatically");}
  Console.WriteLine("PASS: native mask-spawn hook/scoping, ROM cap mesh/emblem in falling and landed death poses, hatless/restored geometry, parked-actor restoration.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();Console.WriteLine("Actual ROM stationary stumble/held recovery passed without opening a game or save.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
