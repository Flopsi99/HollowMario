// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class RemainingPoseChecks {
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

  var conversation=companion.GetType("HollowMario.ConversationMario",true);
  conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
  foreach(string name in new[]{"Tk2dPlayAnimation","Tk2dPlayAnimationWithEvents"})Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HutongGames.PlayMaker.Actions."+name).GetMethod("OnEnter")})!=null,"Native dialogue animation hook installed: "+name);
  var facing=conversation.GetMethod("Facing",S);


  var spells=companion.GetType("HollowMario.SpellMario");spells.GetMethod("Arm",S).Invoke(null,new object[]{hooks,native});
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HeroController").GetMethod("DieFromHazard",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Native hazard hook installed");
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{Assembly.LoadFrom(Path.Combine(managed,"PlayMaker.dll")).GetType("HutongGames.PlayMaker.FsmState").GetMethod("OnEnter",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Native spell state hook installed");
  int poolHooks=0;foreach(var method in game.GetType("ObjectPool").GetMethods(BindingFlags.Static|BindingFlags.Public))if(method.Name=="Spawn"&&method.ReturnType==typeof(GameObject)&&method.GetParameters().Length>0&&method.GetParameters()[0].ParameterType==typeof(GameObject)&&harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{method})!=null)poolHooks++;
  Check(poolHooks==6,"All native pooled hazard-spawn routes covered");
  var planMethod=spells.GetMethod("ForState",S);
  foreach(object[] row in new[]{new object[]{"Fireball Antic",0x67,-1},new object[]{"Fireball 1",0x67,-1},new object[]{"Fireball 2",0x67,-1},new object[]{"Fireball Recoil",0x67,-1},new object[]{"Quake Antic",0x3C,-1},new object[]{"Quake1 Down",0x3D,-1},new object[]{"Quake2 Down",0x3D,-1},new object[]{"Quake1 Land",0x3A,-1},new object[]{"Q2 Land",0x3A,-1},new object[]{"Scream Antic1",0xC0,12},new object[]{"Scream Antic2",0xC0,12},new object[]{"Scream Burst 1",0xC0,12},new object[]{"Scream Burst 2",0xC0,12},new object[]{"Scream End",0xC0,-1}}){object p=planMethod.Invoke(null,new[]{row[0]});Check((int)p.GetType().GetField("Animation",S|BindingFlags.Instance).GetValue(p)==(int)row[1]&&(int)p.GetType().GetField("Hold",S|BindingFlags.Instance).GetValue(p)==(int)row[2],"Correct native spell phase animation and hold");}
  foreach(string unrelated in new[]{"Inactive","Can Cast?","Can Focus?","Has Fireball?"}){object p=planMethod.Invoke(null,new object[]{unrelated});Check(p.GetType().GetField("Level",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==null,"Availability checks/healing do not become spell visuals");}
  var fade=conversation.GetMethod("HazardFade",S);Check((float)fade.Invoke(null,new object[]{0f})==1f&&(float)fade.Invoke(null,new object[]{.6f})==0f&&(float)fade.Invoke(null,new object[]{2f})==0f,"Hazard visual fades once and stays hidden until respawn");
  var shade=companion.GetType("HollowMario.MarioAppearance").GetProperty("ShadeTint",S);Check(((Color)shade.GetValue(null,null)).Equals(Color.black),"Upgraded spells share actual shade-dash tint");
  Console.WriteLine("Spell phase plans, native spell/hazard/pool hooks, shade tint and fade endpoint checks passed");
  var known=conversation.GetMethod("KnownInteraction",S);var mapped=conversation.GetMethod("PoseForClip",S);
  foreach(string owner in new[]{"npc_control","Conversation Control","Shop Region","Inspection","inspect","Stag Bell"})Check((bool)known.Invoke(null,new object[]{owner}),"Native interaction takes visual ownership before approach: "+owner);
  foreach(string owner in new[]{"Bench Control","Spell Control","Control"})Check(!(bool)known.Invoke(null,new object[]{owner}),"Unrelated control handovers are not opted in by name");
  foreach(object[] row in new[]{new object[]{"Walk",0x48},new object[]{"Turn",0xC5},new object[]{"Idle",0xC5},new object[]{"LookUp",0x1E},new object[]{"LookUpEnd",0x1F},new object[]{"LookDown",0xC5},new object[]{"TurnToBG",0x1E},new object[]{"TurnFromBG",0x1F}})Check((int)mapped.Invoke(null,new[]{row[0]})==(int)row[1],"Native approach/read/close animation mapping: "+row[0]);
  Check((int)mapped.Invoke(null,new object[]{"Collect SD 1"})<0,"Special gift animation retains native presentation");
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HutongGames.PlayMaker.Actions.CallMethodProper").GetMethod("OnEnter")})!=null,"Control-taking hook installed before corrective movement");
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{engine.GetType("MarioHollow.MarioRenderer").GetMethod("Render")})!=null,"End handover waits for a rendered gameplay frame");
  var originalControllerType=conversation.GetField("controllerType",S).GetValue(null);var liveRenderer=new object();Controller.Instance=new Controller{Renderer=liveRenderer};conversation.GetField("controllerType",S).SetValue(null,typeof(Controller));
  conversation.GetField("returning",S).SetValue(null,true);conversation.GetField("requested",S).SetValue(null,true);conversation.GetField("oldHidden",S).SetValue(null,false);
  conversation.GetMethod("AfterPlayableRender",S).Invoke(null,new object[]{new object()});
  Check((bool)conversation.GetField("requested",S).GetValue(null),"Unrelated/private renderer cannot reveal the Knight or discard the held image");
  conversation.GetMethod("AfterPlayableRender",S).Invoke(null,new object[]{liveRenderer});
  Check(!(bool)conversation.GetField("requested",S).GetValue(null)&&(bool)conversation.GetField("oldHidden",S).GetValue(null),"First gameplay draw completes the handover with the Knight still hidden");
  conversation.GetField("controllerType",S).SetValue(null,originalControllerType);
  Check((float)facing.Invoke(null,new object[]{-3f,0f,true})<0,"NPC to left selects left-facing profile");Check((float)facing.Invoke(null,new object[]{3f,0f,false})>0,"NPC to right selects right-facing profile");
  Check((float)facing.Invoke(null,new object[]{0f,0f,false})<0,"Vertically aligned NPC uses native facing");
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}




  var maps=spells.GetMethod("ForMapState",S);var mapAngle=conversation.GetMethod("MapFacing",S);var challengeAngle=conversation.GetMethod("ChallengeFacing",S);
  foreach(string state in new[]{"Open Map","Map Idle","Turn L?","Turn R?","Close Map","Get Inventory"}){object p=maps.Invoke(null,new object[]{state});Check((int)p.GetType().GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x3F,"Native map open/idle/turn/close uses carrying idle");}
  foreach(string state in new[]{"Walk Left","Walk Right"}){object p=maps.Invoke(null,new object[]{state});Check((int)p.GetType().GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x18,"Map movement uses slow carrying walk");}
  foreach(bool right in new[]{true,false}){float yaw=(float)mapAngle.Invoke(null,new object[]{right});Check(Math.Abs(Math.Abs(yaw)*180/Math.PI-75)<.001,"Map yaw is 15 degrees toward camera from side profile");float cy=(float)challengeAngle.Invoke(null,new object[]{right});Check(Math.Abs(Math.Abs(cy)*180/Math.PI-165)<.001,"Challenge fist readable at 15 degrees from directly away");}
  var hold=conversation.GetField("actionHeld",S);var group=conversation.GetField("actionGroup",S);var owns=conversation.GetField("actionPose",S);owns.SetValue(null,true);group.SetValue(null,"map");hold.SetValue(null,true);
  conversation.GetMethod("ReleaseActionHold",S).Invoke(null,new object[]{"focus"});Check((bool)hold.GetValue(null),"Unrelated focus idle does not release the map");conversation.GetMethod("ReleaseActionHold",S).Invoke(null,new object[]{"map"});Check(!(bool)hold.GetValue(null),"Native map finish releases map visual ownership");owns.SetValue(null,false);group.SetValue(null,null);
  Console.WriteLine("Map carry idle/walk, camera bias, challenge angle and scoped map ownership passed");
  var death=companion.GetType("HollowMario.DeathMario",true);death.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native});
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HeroController").GetMethod("Die",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Native death prefix installed without replacing its coroutine or timing");
  var nativePlay=game.GetType("tk2dSpriteAnimator").GetMethod("Play",new[]{game.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{nativePlay})!=null,"Underlying native clip route is covered, including look-end clips");
  foreach(var row in new[]{new[]{"Roar Lock","Lock Grounded","116"},new[]{"Roar Lock","Lock Air","2"},new[]{"Dream Nail","Slash","182"},new[]{"Dream Return","Prostrate","135"},new[]{"Dream Return","Get Up","201"},new[]{"Dream Nail","Set","58"},new[]{"Dream Nail","Warp Effect","148"}}){var p=spells.GetMethod("ForOtherState",S).Invoke(null,new object[]{row[0],row[1]});Check((int)p.GetType().GetField("Animation",S|BindingFlags.Instance).GetValue(p)==int.Parse(row[2]),"New native phase selects intended pose: "+row[0]+"/"+row[1]);}
  var pickup=conversation.GetMethod("PickupFacing",S);foreach(bool right in new[]{false,true}){float yaw=(float)pickup.Invoke(null,new object[]{right});Check(Math.Abs(yaw)<.001,"Item pickup faces the camera from either movement direction");}
  var side=conversation.GetMethod("SideFacing",S);foreach(bool right in new[]{true,false}){float a=(float)side.Invoke(null,new object[]{right,false}),z=(float)side.Invoke(null,new object[]{right,true});Check(Math.Abs(Math.Abs(a)*180/Math.PI-75)<.001&&Math.Abs(Math.Abs(z)*180/Math.PI-105)<.001,"Healing faces sideways toward camera, pickups sideways away, in both directions");}
  var rate=conversation.GetMethod("WalkRate",S);Check((float)rate.Invoke(null,new object[]{0x48,7.5f})==3f&&(float)rate.Invoke(null,new object[]{0x18,2.5f})==2f,"Walking pace increases with native transition/map movement");
  Check((float)rate.Invoke(null,new object[]{0x67,7.5f})==1f,"Walking pace never speeds up spells or other gestures");
  var invul=plugin.GetMethod("EndsHazardProtectionWithRespawn",S);Check((bool)invul.Invoke(null,new object[]{true,true,true})&&!(bool)invul.Invoke(null,new object[]{false,true,true})&&!(bool)invul.Invoke(null,new object[]{true,true,false}),"Only Mario's hazard recovery uses bounded respawn protection");
  var distance=death.GetMethod("FallDistance",S);object[] fall={0f,-30f,.1f};float drop=(float)distance.Invoke(null,fall);Check(Math.Abs(drop+.15f)<.00001&&Math.Abs((float)fall[0]+3f)<.00001,"Death visual starts falling under gravity without moving native Hero");for(int i=0;i<300;i++){fall[2]=.02f;drop=(float)distance.Invoke(null,fall);Check(!float.IsNaN(drop)&&drop<0,"A bottomless fall stays finite and never becomes a grounded death");}
  var focus=companion.GetType("HollowMario.KnightFocus",true);var fakeHero=new object();MapController.Instance=new MapController{hero=fakeHero};focus.GetMethod("Arm",S).Invoke(null,new object[]{typeof(MapController)});focus.GetMethod("MapPresentation",S).Invoke(null,new object[]{MapController.Instance,fakeHero});Check(Convert.ToString(focus.GetField("owner",S).GetValue(null))=="Map"&&ReferenceEquals(focus.GetField("focusHero",S).GetValue(null),fakeHero),"Map ownership is recorded before presentation deactivates gameplay Mario");focus.GetMethod("Forget",S).Invoke(null,null);focus.GetMethod("Arm",S).Invoke(null,new object[]{engine.GetType("MarioHollow.MarioController")});
  foreach(string phase in new[]{"Lock Start","Flip","Set Push Direction","Check On Ground","Lock Grounded","Lock Air"}){
   var plan=spells.GetMethod("ForOtherState",S).Invoke(null,new object[]{"Roar Lock",phase});
   Check((string)plan.GetType().GetField("Level",S|BindingFlags.Instance).GetValue(plan)=="roar","Every native roar phase is covered before the final stun: "+phase);
   foreach(bool grounded in new[]{false,true})Check((int)spells.GetMethod("RoarAnimation",S).Invoke(null,new object[]{phase,grounded})==(phase=="Lock Air"?2:phase=="Lock Grounded"?116:grounded?116:2),"Roar lead-in selects air/ground pose from the current body");
  }
  Check((bool)spells.GetMethod("ReleasesHold",S).Invoke(null,new object[]{"Regain Control"}),"Native roar completion releases its presentation hold");
  Console.WriteLine("Death/head-return hooks, new state plans, viewing angles, movement pace, hazard protection and map ownership checks passed");
  var restAudio=companion.GetType("HollowMario.BenchRestAudio",true);restAudio.GetMethod("Install",S).Invoke(null,new object[]{library});
  foreach(int clip in new[]{0x74,0x02,0x87,0xC9,0xB6,0x03,0x27,0x84,0x85,0xC8})using(var newPose=new OpeningMarioPose(native,library,0f,true)){
   for(int frame=0;frame<90;frame++){
    newPose.TalkFacing(.9f,clip==0x87);if(clip==0x74)newPose.LimitAnimationFrame(clip,4);
    int old=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});bool q=(bool)restAudio.GetMethod("Begin",S).Invoke(null,null);try{b=newPose.Tick(b,true);}finally{restAudio.GetMethod("End",S).Invoke(null,new object[]{q});flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{old});}TestEnvironment.PullAudio(audioArgs);
    Check(Convert.ToInt32(newPose.State.GetType().GetField("animID").GetValue(newPose.State))==clip&&Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"New ROM pose produces geometry: "+clip);
   }
  }
  restAudio.GetMethod("Dispose",S).Invoke(null,null);
  Console.WriteLine("Actual ROM roar, dream sleep/wake, sweeping kick, death, pickup and ground-sitting geometry passed");
  int heldFrames=0;
  for(int visit=0;visit<4;visit++){
   float angle=(float)(visit<2?mapAngle:challengeAngle).Invoke(null,new object[]{visit%2==0});
   using(var pose=new OpeningMarioPose(native,library,0f,true)){
    pose.TalkFacing(angle);
    for(int frame=0;frame<200;frame++){
     int expected=visit==0?0x3F:visit==1?0x18:frame<170?0x67:0xC5;
     if(expected==0x67)pose.LimitAnimationFrame(expected,4);
     int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
     var t=pose.State.GetType();int af=Convert.ToInt32(t.GetField("animFrame").GetValue(pose.State));Check(Convert.ToInt32(t.GetField("animID").GetValue(pose.State))==expected,"Actual ROM carrying idle/walk or punch/idle geometry");
     if(expected==0x67){Check(af<=4,"Challenge punch remains at full extension");if(af==4)heldFrames++;}
     Check(Math.Abs(Convert.ToSingle(t.GetField("faceAngle").GetValue(pose.State))-angle)<.002f,"Actual native map/challenge angle");Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"ROM geometry remains available throughout hold and return");
    }
   }
  }
  Check(heldFrames>300,"Challenge punch holds without repeated punching, then returns to idle");
  Console.WriteLine("Actual ROM carry idle/slow walk, sustained extended punch and ending, both map/challenge directions, and safe repeated actor reuse passed");
flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Remaining presentation checks passed without opening a game or save.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
