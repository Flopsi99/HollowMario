// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class TransitionChecks {
 class Controller {public static Controller Instance;public object Renderer;}
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

  var conversation=companion.GetType("HollowMario.ConversationMario",true);
  conversation.GetMethod("Arm",S).Invoke(null,new object[]{hooks,engine.GetType("MarioHollow.MarioController"),native,logger});
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
  foreach(string name in new[]{"Tk2dPlayAnimation","Tk2dPlayAnimationWithEvents"})Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HutongGames.PlayMaker.Actions."+name).GetMethod("OnEnter")})!=null,"Native dialogue animation hook installed: "+name);
  var facing=conversation.GetMethod("Facing",S);


  foreach(string name in new[]{"EnterScene","LeaveScene"})Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HeroController").GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})!=null,"Native transition hook installed: "+name);
  Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType("HeroAnimationController").GetMethod("PlayClip")})!=null,"Door entry animation timing hook installed");
  var gp=game.GetType("GlobalEnums.GatePosition");foreach(object[] item in new[]{new object[]{"top",0},new object[]{"right",1},new object[]{"left",2},new object[]{"bottom",3},new object[]{"door",4}})Check(Convert.ToInt32(Enum.Parse(gp,(string)item[0]))==(int)item[1],"Gate mapping matches actual installed game");
  var visible=conversation.GetMethod("TransitionVisible",S);
  foreach(bool nativeVisible in new[]{false,true}){
   Check((bool)visible.Invoke(null,new object[]{false,false,nativeVisible}),"Outgoing airborne transition remains Mario even with a hidden native sprite");
   Check((bool)visible.Invoke(null,new object[]{true,false,nativeVisible}),"Active incoming jump/fall does not inherit disabled Knight visibility");
  }
  Check(!(bool)visible.Invoke(null,new object[]{true,true,false}),"Intentional hidden pre-entry wait remains hidden");
  Check((bool)visible.Invoke(null,new object[]{true,true,true}),"Native-visible pre-entry pose remains visible");
  var transitionEnum=game.GetType("GlobalEnums.HeroTransitionState",true);
  Check(Enum.IsDefined(transitionEnum,"WAITING_TO_ENTER_LEVEL"),"Visibility phase matches the installed game's actual enum");
  var choose=conversation.GetMethod("TransitionAnimation",S);var face=conversation.GetMethod("TransitionFacing",S);
  foreach(int gate in new[]{0,3})Check((int)choose.Invoke(null,new object[]{gate,true,false,true,0f,0f})==0xC5,"Vertical entry stops falling when grounded, before input returns");
  var landing=conversation.GetMethod("TransitionLanding",S);
  Check((int)landing.Invoke(null,new object[]{0x56,true,true,false})==0x96,"Native hard landing uses crouch get-up instead of the seated landing");
  Check((int)landing.Invoke(null,new object[]{0x56,true,false,true})==0x57,"Native soft landing plays while entry still owns control");
  Check((int)landing.Invoke(null,new object[]{0x56,false,true,true})==0x56,"An airborne entry cannot land from a stale native clip");
  var ground=conversation.GetMethod("TransitionGrounded",S);
  Check((bool)ground.Invoke(null,new object[]{false,true,0f}),"Physical floor contact settles an entry before the stale native onGround flag");
  Check(!(bool)ground.Invoke(null,new object[]{false,true,8f})&&!(bool)ground.Invoke(null,new object[]{false,true,-8f}),"Passing a nearby floor during a jump/fall cannot start the landing pose");
  Check(!(bool)ground.Invoke(null,new object[]{false,false,0f}),"A midair pause without floor contact remains airborne");
  var entryLand=conversation.GetMethod("EntryLanding",S);
  foreach(bool hard in new[]{false,true}){
   Check((int)entryLand.Invoke(null,new object[]{0xC5,true,true,true,hard,0f,.2f})==(hard?0x96:0x57),"A completed entry fall plays its landing before input returns");
   Check((int)entryLand.Invoke(null,new object[]{0xC5,true,true,true,hard,.2f,.2f})==0xC5,"Landing recovers into idle instead of holding the fall until control");
  }
  Check((int)entryLand.Invoke(null,new object[]{0x56,true,false,true,false,0f,.2f})==0x56,"An unsupported entry keeps falling");
  Check((int)entryLand.Invoke(null,new object[]{0x48,true,true,false,false,0f,.2f})==0x48,"Normal walking entries do not receive an invented landing");
  var transitionAction=conversation.GetMethod("TransitionAction",S);
  Check((int)transitionAction.Invoke(null,new object[]{0x3A,true,false})==0x02,"Damage keeps priority over transition landing");
  foreach(object[] row in new[]{
   new object[]{2,true,false,true,6f,0f,0x48},new object[]{1,true,false,true,-6f,0f,0x48},
   new object[]{2,false,false,true,-6f,0f,0x48},new object[]{1,false,false,true,6f,0f,0x48},
   new object[]{0,false,false,false,0f,0f,0x4D},new object[]{3,false,false,false,0f,0f,0x56},
   new object[]{0,true,false,false,0f,-5f,0x56},new object[]{3,true,false,false,3f,8f,0x4D},
   new object[]{3,true,false,false,3f,-2f,0x56},new object[]{1,false,false,false,6f,5f,0x4D},
   new object[]{2,false,false,false,-6f,-5f,0x56},new object[]{2,true,false,true,0f,0f,0xC5},
   new object[]{4,true,true,true,0f,0f,0x48},new object[]{4,false,true,true,0f,0f,0x48},
   new object[]{4,true,false,true,0f,0f,0xC5}}){var input=new object[6];Array.Copy(row,input,6);Check((int)choose.Invoke(null,input)==(int)row[6],"Grounded, aerial, vertical and door transition animation selection");}
  Check((float)face.Invoke(null,new object[]{4,true,0f,false})==0f,"Door exit faces camera");Check(Math.Abs((float)face.Invoke(null,new object[]{4,false,0f,true})-(float)Math.PI)<.001f,"Door entry faces away");
  Check((float)face.Invoke(null,new object[]{2,true,6f,false})>0,"Movement chooses right-facing side profile");Check((float)face.Invoke(null,new object[]{1,true,-6f,true})<0,"Movement chooses left-facing side profile");
  var opening=companion.GetType("HollowMario.OpeningMario");var pending=opening.GetField("pending",S);pending.SetValue(null,true);Check((bool)opening.GetProperty("OwnsSequence",S).GetValue(null,null),"New-game opening retains presentation ownership before its actor exists");pending.SetValue(null,false);
  Console.WriteLine("Actual transition/door hooks, installed gate enum mapping, 15 ground/air/vertical/door cases, all facing directions and new-game exclusion passed");
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
  var recovery=companion.GetType("HollowMario.ArrivalMario",true).GetMethod("HardLandingFrame",S);
  var contracts=engine.GetType("MarioHollow.BridgeContracts",true);
  var framePolicy=contracts.GetField("landingFrame",S);
  framePolicy.SetValue(null,Delegate.CreateDelegate(framePolicy.FieldType,recovery));
  contracts.GetField("pushAnimation",S).SetValue(null,flourish.GetMethod("PushCinematicAnimation",S));
  contracts.GetField("popAnimation",S).SetValue(null,flourish.GetMethod("PopCinematicAnimation",S));
  foreach(float duration in new[]{.1f,.2f,.35f,.6f})using(var recoveryPose=new OpeningMarioPose(native,library,0f,true)){
   recoveryPose.TalkFacing(1.57f);int id=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(recoveryPose),last=-1;
   for(int tick=0;tick/30f<duration;tick++){
    float age=tick/30f;int expected=(int)recovery.Invoke(null,new object[]{age,duration});
    int prior=(int)contracts.GetMethod("BeginTimedLanding").Invoke(null,new object[]{id,age,duration});
    try{b=recoveryPose.Tick(b,true);}finally{contracts.GetMethod("EndLandingAnimation").Invoke(null,new object[]{prior});}
    TestEnvironment.PullAudio(audioArgs);
    var state=recoveryPose.State;int actual=Convert.ToInt32(state.GetType().GetField("animFrame").GetValue(state));
    Check(Convert.ToInt32(state.GetType().GetField("animID").GetValue(state))==0x96&&actual==expected,"Real ROM crouch get-up follows the recovery clock");
    Check(actual>=last&&actual<=9,"Crouch get-up progresses without looping or reversing");last=actual;
    Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Recovery produces actual ROM model geometry");
   }
   Check(last==9,"The full get-up finishes before short and long native recovery windows end");
   b=recoveryPose.Tick(b,true);Check(Convert.ToInt32(recoveryPose.State.GetType().GetField("animID").GetValue(recoveryPose.State))!=0x96,"The landing override releases normally afterwards");
  }

  int previousId=-1;
  for(int visit=0;visit<4;visit++){
   float angle=visit==2?0f:visit==3?(float)Math.PI:(float)facing.Invoke(null,new object[]{visit%2==0?-3f:3f,0f,true});
   using(var pose=new OpeningMarioPose(native,library,0f,true)){
    int id=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(pose);
    if(previousId>=0)Check(id==previousId,"Repeated conversations reuse the safely parked actor");previousId=id;
    pose.TalkFacing(angle);
    for(int frame=0;frame<140;frame++){
     int expected=frame<30?0x48:frame<60?0x4D:frame<100?0x56:frame<120?0xC5:0x48;
     int before=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});
     try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{before});}
     TestEnvironment.PullAudio(audioArgs);var t=pose.State.GetType();
     Check(Convert.ToInt32(t.GetField("animID").GetValue(pose.State))==expected,"Real ROM geometry uses look-up/closing animation");
     Check(Math.Abs(Math.IEEERemainder(Convert.ToSingle(t.GetField("faceAngle").GetValue(pose.State))-angle,2*Math.PI))<.002f,"Native pose faces the speaker");
     Check(Convert.ToUInt32(t.GetField("action").GetValue(pose.State))==0x0000130Au,"Dialogue pose remains stationary instead of sleeping/falling");
     Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Real player-ROM geometry produced");
    }
   }
   Check(TestEnvironment.ParkedActors>0,"Native actor retained safely after conversation");
  }
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Transition hooks and selection, real walk/jump/fall/idle ROM geometry in left/right/front/back directions, safe repeated actor reuse and interaction handover checks passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
