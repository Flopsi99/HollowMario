// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class UpgradeKnockoutChecks {
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







  var upgrade=companion.GetType("HollowMario.UpgradeKnockoutMario",true);var phase=upgrade.GetMethod("Phase",S);var at=upgrade.GetMethod("At",S);
  foreach(object[] expected in new[]{new object[]{"Collect Magical 1",1},new object[]{"Collect Magical 2",1},new object[]{"Collect Magical 3",2},new object[]{"Collect Magical Fall",3},new object[]{"Collect Magical Land",4},new object[]{"Prostrate",5},new object[]{"Prostrate Rise",6}})Check((int)phase.Invoke(null,new object[]{expected[0],true})==(int)expected[1],"Every native Shaman phase is mapped");
  foreach(string other in new[]{"Collect Normal 1","Collect SD 1","Prostrate","Prostrate Rise","Idle"})Check((int)phase.Invoke(null,new object[]{other,false})==0,"Other pickups and unowned wake-ups stay unchanged");
  var conversation=companion.GetType("HollowMario.ConversationMario",true);conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var nativePlay=game.GetType("tk2dSpriteAnimator").GetMethod("Play",new[]{game.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});
  object info=harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{nativePlay});bool hookFound=false;foreach(object patch in (System.Collections.IEnumerable)info.GetType().GetField("Postfixes").GetValue(info)){var method=(MethodInfo)patch.GetType().GetProperty("PatchMethod").GetValue(patch,null);if(method.DeclaringType==upgrade)hookFound=true;}Check(hookFound,"Actual cutscene-animation entry is hooked");
  var followsHero=upgrade.GetMethod("CanFollowHero",S);
  Check((bool)followsHero.Invoke(null,new object[]{3,false})&&(bool)followsHero.Invoke(null,new object[]{4,false}),"A knockdown on the real Hero starts without requiring a temporary cutscene double");
  Check(!(bool)followsHero.Invoke(null,new object[]{1,false})&&!(bool)followsHero.Invoke(null,new object[]{6,false}),"Ordinary pickups and unowned wake-ups cannot start a new knockout");
  Check((int)phase.Invoke(null,new object[]{"Dreamer Land",true})==4,"Dreamers landing continues the shared knockdown handler");
  var statePhase=upgrade.GetMethod("StatePhase",S);
  Check((int)statePhase.Invoke(null,new object[]{"Get Fireball","Get"})==2,"Shaman impact has an independent native-state trigger");
  Check((int)statePhase.Invoke(null,new object[]{"Check Fall","Black"})==8&&(int)statePhase.Invoke(null,new object[]{"Check Fall","Wait For Input"})==8,"Cell starts and holds belly-prone independently of destroyed proxy");
  Check((int)statePhase.Invoke(null,new object[]{"Check Fall","Get Up"})==9&&(int)statePhase.Invoke(null,new object[]{"Enemy Control","Get Up"})==0,"Cell rise stays scoped to actual Shaman sequence");
  var charms=companion.GetType("HollowMario.AttackCharmEffects",true);var ct=charms.GetNestedType("Cycle",BindingFlags.NonPublic);var observe=ct.GetMethod("Observe",S|BindingFlags.Instance);object attackCycle=Activator.CreateInstance(ct,true);
  Func<string,int,int,bool,bool> swing=(move,animation,f,allowed)=>(bool)observe.Invoke(attackCycle,new object[]{move,animation,f,allowed});
  Check(swing("punch",1,0,true)&&!swing("punch",1,1,true)&&swing("punch",2,0,true),"One charm trigger per punch, including punch combo");
  Check(swing("dive",3,0,true)&&!swing("dive_slide",4,0,true)&&!swing("dive_slide",4,0,true),"Dive-to-slide never retriggers");
  Check(swing("ground_pound",5,0,true)&&!swing("ground_pound_land",6,0,true),"Pound landing stays in same attack");
  Check(!swing(null,0,0,true)&&swing("jump_kick",7,0,true)&&!swing("jump_kick",7,1,true),"Leaving attack rearms next kick");
  Check(!swing("slide_kick",8,0,false)&&swing("slide_kick",8,1,true)&&!swing("slide_kick_slide",9,0,true),"Damage gating and continuing slide trigger once");
  var beamMove=charms.GetMethod("BeamMove",S);
  foreach(string move in new[]{"punch","move_punch","jump_kick","slide_kick"})Check((bool)beamMove.Invoke(null,new object[]{move}),"Punch/kick beam eligibility");
  foreach(string move in new[]{"dive","dive_slide","slide_kick_slide","ground_pound","ground_pound_land","idle"})Check(!(bool)beamMove.Invoke(null,new object[]{move}),"No slide, dive or pound beam");
  var ordinary=charms.GetMethod("OrdinaryBeam",S);var fury=charms.GetMethod("FuryBeam",S);
  Check((bool)ordinary.Invoke(null,new object[]{5,5,false,false})&&!(bool)ordinary.Invoke(null,new object[]{4,5,false,false})&&!(bool)ordinary.Invoke(null,new object[]{1,1,true,false})&&(bool)ordinary.Invoke(null,new object[]{1,1,true,true}),"Native full-health/Joni eligibility");
  Check((bool)fury.Invoke(null,new object[]{1,0,true})&&!(bool)fury.Invoke(null,new object[]{1,1,true})&&!(bool)fury.Invoke(null,new object[]{1,0,false}),"Native Fury/blue-health eligibility");
  Check(game.GetType("ObjectPoolExtensions").GetMethod("Spawn",new[]{typeof(GameObject),typeof(Vector3)})!=null,"Exact native projectile pool method exists");
  foreach(string field in new[]{"grubberFlyBeamPrefabL","grubberFlyBeamPrefabR","grubberFlyBeamPrefabL_fury","grubberFlyBeamPrefabR_fury","joniBeam","MANTIS_CHARM_SCALE"})Check(game.GetType("HeroController").GetField(field,S|BindingFlags.Instance|BindingFlags.Public)!=null,"Native beam member exists: "+field);
  var sample=at.ReturnType;var anim=sample.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var frameField=sample.GetField("Frame",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  object cell=at.Invoke(null,new object[]{8,1f,0f}),waking=at.Invoke(null,new object[]{9,0f,0f});
  Check((int)anim.GetValue(cell)==0xC9&&(int)frameField.GetValue(cell)==0&&(int)anim.GetValue(waking)==0xC9&&(int)frameField.GetValue(waking)==0,"Cell lying hold matches original wake-up's first frame exactly");
  var poseHeight=new System.Collections.Generic.Dictionary<int,float>();
  var observed=new System.Collections.Generic.HashSet<int>();
  for(int cycle=0;cycle<2;cycle++)using(var pose=new OpeningMarioPose(native,library,0f,true)){
   for(int step=0;step<300;step++){
    int stage=step<90?1:step<120?2:step<150?3:step<180?4:step<210?5:step<240?6:step<270?8:9;
    float progress=stage==1?step/90f:((step-90)%30)/29f;
    object target=at.Invoke(null,new object[]{stage,progress,step/30f});int clip=(int)anim.GetValue(target),frame=(int)frameField.GetValue(target);
    pose.TalkFacing(0f);int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
    try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
    var st=pose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(pose.State))==clip&&Convert.ToInt32(st.GetField("animFrame").GetValue(pose.State))==frame,"Exact ROM stage/frame, including switch from dance to hit to strong backwards fall");
    Check(Math.Abs(Convert.ToSingle(st.GetField("faceAngle").GetValue(pose.State)))<.001f,"Model stays facing camera throughout");
    Check(Math.Abs(Convert.ToSingle(st.GetField("px").GetValue(pose.State))+6000)<.01&&Math.Abs(Convert.ToSingle(st.GetField("py").GetValue(pose.State))+6000)<.01,"Private animation actor never takes over the cutscene's physics");
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Actual ROM mesh exists in every phase");observed.Add(clip);
    int triangleCount=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));
    float[] vertices=new float[triangleCount*9];Marshal.Copy((IntPtr)bt.GetField("position").GetValue(b),vertices,0,vertices.Length);float low=float.PositiveInfinity,high=float.NegativeInfinity;
    for(int v=1;v<vertices.Length;v+=3){low=Math.Min(low,vertices[v]);high=Math.Max(high,vertices[v]);}
    if(progress>=.999f)poseHeight[stage]=high-low;
    if(stage==2&&progress>.25f)Check(clip==0x01&&frame<=20,"Knockback ends before built-in stand-up");
    if(stage==3||stage==4||stage==5)Check(clip==0x01&&frame<=37,"Falling/landing/prone never reach standing recovery frames");
    if(stage==8)Check(clip==0xC9&&frame==0,"Cell holds actual lying pose for entire wait");
    if(stage==9)Check(clip==0xC9,"Original wake-up animation retained");

   }
  }
  Check(poseHeight[5]<poseHeight[9]*.65f&&poseHeight[8]<poseHeight[9]*.65f,"Actual ROM mesh is low/prone during holds and standing only after wake-up");
  foreach(int expected in new[]{0xCD,0x74,0x01,0xC9})Check(observed.Contains(expected),"Dance, impact, strong fall, prone and wake animation reached");
  var backAt=upgrade.GetMethod("BackAt",S);float proneHeight=0,stoodHeight=0;
  using(var pose=new OpeningMarioPose(native,library,0f,true))foreach(int stage in new[]{3,4,5,6})for(int step=0;step<30;step++){
   object target=backAt.Invoke(null,new object[]{stage,step/29f});int clip=(int)anim.GetValue(target),frame=(int)frameField.GetValue(target);
   Check(clip==0x01,"Dreamer knockdown, floor hold and recovery all use the same back-fall clip, never a dance");
   Check(stage!=5||frame==37,"He stays on his back throughout the native dialogue hold");
   pose.TalkFacing(0f);int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
   try{if(Convert.ToInt32(pose.State.GetType().GetField("animID").GetValue(pose.State))!=clip)b=pose.Tick(b,true);pose.SetAnimationFrame((short)(frame-1));b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
   Check(Convert.ToInt32(pose.State.GetType().GetField("animFrame").GetValue(pose.State))==frame,"Native knockdown follows the host's fall/land/hold/rise clock");
   if(step==29&&(stage==5||stage==6)){
    int count=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));var points=new float[count*9];Marshal.Copy((IntPtr)bt.GetField("position").GetValue(b),points,0,points.Length);float low=float.MaxValue,high=float.MinValue;for(int i=1;i<points.Length;i+=3){low=Math.Min(low,points[i]);high=Math.Max(high,points[i]);}
    if(stage==5)proneHeight=high-low;else stoodHeight=high-low;
   }
  }
  Check(proneHeight>0&&proneHeight<stoodHeight*.75f,"Actual ROM model stays low through the speech and stands only during the native get-up cue");
  Console.WriteLine("PASS Dreamer real-Hero knockdown entry, landing, continuous back-prone hold and same-clip recovery geometry.");
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pinned in pins)pinned.Free();
  Console.WriteLine("Cutscene hook, all Shaman phases, normal-pickup exclusion, actual ROM star dance/hit/backward-fall/knockout/wake frames, front-facing model and independent physics checks passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
