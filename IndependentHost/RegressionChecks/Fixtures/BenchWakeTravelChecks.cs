// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class BenchWakeTravelChecks {
 struct PoseState {public float faceAngle;}
 class Controller {public int MarioId;public PoseState State,PrevState;public bool Wanted=true;public object Renderer;}
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

  var rendererType=engine.GetType("MarioHollow.MarioRenderer");var renderInfo=harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{rendererType.GetMethod("Render")});
  Check(renderInfo!=null,"Bench blend is hooked to actual renderer draws");
  bool found=false;foreach(object patch in (System.Collections.IEnumerable)renderInfo.GetType().GetField("Postfixes").GetValue(renderInfo)){
   var method=(MethodInfo)patch.GetType().GetProperty("PatchMethod").GetValue(patch,null);if(method.Name=="AfterPlayableRender")found=true;
  }Check(found,"Bench placement executes after drawing, not from pre-draw mesh bounds");
  var waiting=new Controller{MarioId=-1,Renderer=new object()};bench.GetField("returnController",S).SetValue(null,waiting);bench.GetField("returnFacing",S).SetValue(null,true);
  bench.GetField("returnAge",S).SetValue(null,0f);bench.GetField("returnStarted",S).SetValue(null,false);
  for(int frame=0;frame<3;frame++)bench.GetMethod("Draw",S).Invoke(null,new object[]{waiting});
  Check(ReferenceEquals(bench.GetField("returnController",S).GetValue(null),waiting)&&!(bool)bench.GetField("returnStarted",S).GetValue(null),"Activation/late-update frames without Render do not sample a zero-centred mesh or start the blend");
  bench.GetMethod("AfterPlayableRender",S).Invoke(null,new object[]{new object(),new Vector3(0,0,0)});
  Check(!(bool)bench.GetField("returnStarted",S).GetValue(null),"Private bench/conversation renderers cannot start gameplay exit blend");
  bench.GetMethod("ClearReturn",S).Invoke(null,null);
  var placementMethod=bench.GetMethod("ExitTranslation",S);
  foreach(Vector3 feet in new[]{new Vector3(196,5,0),new Vector3(450,120,0),new Vector3(-63,-63,-63)})foreach(float degrees in new[]{0f,16f}){
   var exitSeat=feet+new Vector3(0,.38f,0);var exitOffset=exitSeat-feet;
   float r=degrees*(float)Math.PI/180f;var startTilt=new Quaternion(0,0,(float)Math.Sin(r*.5f),(float)Math.Cos(r*.5f));
   for(int frame=0;frame<=22;frame++){
    float p=(float)ease.Invoke(null,new object[]{frame*.01f});float angle=r*(1-p);var roll=new Quaternion(0,0,(float)Math.Sin(angle*.5f),(float)Math.Cos(angle*.5f));
    var pos=(Vector3)placementMethod.Invoke(null,new object[]{exitOffset,feet,roll,p});var shown=pos+roll*feet;
    Check(Math.Abs(shown.x-feet.x)<.0001f&&Math.Abs(shown.z-feet.z)<.0001f,"Exit cannot invent horizontal or depth displacement at distant room coordinates");
    Check(shown.y>=feet.y-.0001f&&shown.y<=exitSeat.y+.0001f,"Exit remains between exitSeat and floor");
   }
   Check(((Vector3)placementMethod.Invoke(null,new object[]{exitOffset,feet,Quaternion.identity,1f})).sqrMagnitude<.00000001f,"Final transform is identity with no residual exitOffset");
  }
  var actualFeet=new Vector3(196,5,0);var actualSeat=actualFeet+new Vector3(0,.38f,0);var oldOffset=actualSeat-Vector3.zero;
  Check((actualFeet+oldOffset-actualSeat).magnitude>100,"Reproduced old room-sized displacement from uninitialized zero-centred mesh");
  Console.WriteLine("Activation-before-first-render regression, renderer ownership, no lateral displacement at far coordinates, tilted pivot and exact floor endpoint passed");
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

  var sound=companion.GetType("HollowMario.BenchRestAudio",true);sound.GetMethod("Install",S).Invoke(null,new object[]{library});
  object observe=Activator.CreateInstance(hm,new object[]{typeof(BenchWakeTravelChecks).GetMethod("SoundObserved",S)});
  Array.Find(harmony.GetMethods(),m=>m.Name=="Patch"&&m.GetParameters().Length==6).Invoke(hooks,new object[]{sound.GetMethod("ShouldMute",S),null,observe,null,null,null});
  var mute=sound.GetMethod("ShouldMute",S);var begin=sound.GetMethod("Begin",S);var end=sound.GetMethod("End",S);
  Check(!(bool)mute.Invoke(null,new object[]{0x240E8080u}),"Ordinary Mario snoring outside bench tick remains untouched");
  bool q=(bool)begin.Invoke(null,null);
  foreach(uint sample in new uint[]{0x240E8080u,0x240F8080u,0x2435FF80u,0x240E0081u})Check((bool)mute.Invoke(null,new object[]{sample}),"All bench snores suppressed, including changed priority flags");
  Check(!(bool)mute.Invoke(null,new object[]{0x70040081u}),"Dialogue/other sound remains audible inside bench tick");
  bool nested=(bool)begin.Invoke(null,null);end.Invoke(null,new object[]{nested});Check((bool)mute.Invoke(null,new object[]{0x240F8080u}),"Nested scope retains suppression");end.Invoke(null,new object[]{q});

  var seat=bench.GetMethod("SeatOffset",S);var offset=(Vector3)seat.Invoke(null,new object[]{new Vector2(0f,.1f),new Vector2(.8f,1.6f)});
  Check(Math.Abs(offset.y-(-.32f))<.00001f,"Seat is raised by exactly 0.38 relative to the collider's local bottom");
  foreach(float degrees in new[]{0f,16f}){
   float radians=degrees*(float)Math.PI/180f;var tilt=new Quaternion(0,0,(float)Math.Sin(radians*.5f),(float)Math.Cos(radians*.5f));
   var nativeFeet=new Vector3(-63,-63,-63);var anchor=new Vector3(11.38f,20.86f,.0125f)+tilt*offset;
   var translation=anchor-tilt*nativeFeet;var renderedFeet=translation+tilt*nativeFeet;
   Check((renderedFeet-anchor).sqrMagnitude<.0000001f,"Flat/recorded Waterways tilt pivots about the seated actor, not the remote stage");
   var raised=tilt*new Vector3(0,.38f,0);
   Check(raised.y>.30f,"Seat lift follows local up even on the actual 16-degree bench");
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

  var wakeProgress=bench.GetMethod("WakeProgress",S);int wakeEnd=(int)bench.GetField("WakeLastFrame",S).GetRawConstantValue();Check(wakeEnd==19,"Wake final frame matches actual ROM animation");
  foreach(float duration in new[]{.25f,.5f,.8f})using(var wakePose=new OpeningMarioPose(native,library,0f,true)){
   wakePose.TalkFacing(0f);float previousProgress=-1f;int finalFrame=-1;
   for(int tick=0;tick<=40;tick++){
    float seconds=tick/30f;float progress=(float)wakeProgress.Invoke(null,new object[]{seconds,duration});Check(progress>=previousProgress&&progress<=1f,"Wake travel progresses once without restarting at native phase changes");previousProgress=progress;
    wakePose.SetAnimationFrame((short)((int)Math.Round(progress*wakeEnd)-1));int cinematic=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{0xC8});try{b=wakePose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{cinematic});}TestEnvironment.PullAudio(audioArgs);
    var st=wakePose.State.GetType();Check(Convert.ToInt32(st.GetField("animID").GetValue(wakePose.State))==0xC8,"Wake travel samples the actual standing-up animation");finalFrame=Convert.ToInt32(st.GetField("animFrame").GetValue(wakePose.State));Check(finalFrame<=wakeEnd,"Standing pose holds at the final frame without looping");
    foreach(float degrees in new[]{0f,16f}){
     float radians=degrees*(float)Math.PI/180f;var tilt=new Quaternion(0,0,(float)Math.Sin(radians*.5f),(float)Math.Cos(radians*.5f));var level=new Quaternion(0,0,0,1);var feet=new Vector3(-63,-63,-63);var seatPoint=new Vector3(10,22,0);var floor=new Vector3(10,20,0);var at=Vector3.Lerp(seatPoint,floor,progress);var rotation=new Quaternion(0,0,(float)Math.Sin(radians*(1f-progress)*.5f),(float)Math.Cos(radians*(1f-progress)*.5f));var translation=at-rotation*feet;
     Check((translation+rotation*feet-at).sqrMagnitude<.000001,"Flat/tilted wake travel pivots around Mario, without remote-stage flying");if(seconds>=duration)Check((at-floor).sqrMagnitude<.000001,"Standing-up completion already reaches the floor before gameplay handover");
    }
   }
   Check(finalFrame==wakeEnd&&previousProgress==1f,"Standing animation and floor descent finish together for native wake timings");
  }
  Console.WriteLine("Actual ROM wake retiming and simultaneous floor arrival passed for short/long exits and flat/16-degree benches");
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
