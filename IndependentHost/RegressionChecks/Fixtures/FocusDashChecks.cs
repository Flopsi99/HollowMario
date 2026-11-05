// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class FocusDashChecks {
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



  var dashPlan=spells.GetMethod("ForDashState",S);
  foreach(string state in new[]{"Start Slug Anim","Focus Start","Focus","Focus Heal","Focus S","Focus Left","Focus Right","Focus Heal 2"}){
   object p=planMethod.Invoke(null,new object[]{state});var t=p.GetType();Check((int)t.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x97&&(int)t.GetField("Hold",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==2&&(bool)t.GetField("HoldControl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p),"Native normal/slug healing holds partial crouch and visual ownership");
  }
  foreach(string state in new[]{"Ground Charge","Ground Charged","Wall Charge","Wall Charged"}){
   object p=dashPlan.Invoke(null,new object[]{state});var t=p.GetType();Check((int)t.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x97&&(int)t.GetField("Hold",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==2&&(bool)t.GetField("HoldControl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p),"Actual grounded/wall crystal charge phases hold partial crouch");
  }
  foreach(string state in new[]{"Dash Start","Dashing","Cancelable","Enter Super Dash","Enter Velocity"}){
   object p=dashPlan.Invoke(null,new object[]{state});var t=p.GetType();Check((int)t.GetField("Animation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p)==0x8C&&(bool)t.GetField("HoldControl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p),"Release/long flight/room entry keeps slide-kick presentation");
  }
  foreach(string state in new[]{"Air Cancel","Hit Wall","Charge Cancel Ground","Charge Cancel Wall"}){object p=dashPlan.Invoke(null,new object[]{state});Check((bool)p.GetType().GetField("HoldControl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(p),"Cancellation/impact stays covered until native Superdash returns control");}
  var hold=conversation.GetField("actionHeld",S);var group=conversation.GetField("actionGroup",S);var owns=conversation.GetField("actionPose",S);
  owns.SetValue(null,true);group.SetValue(null,"focus");hold.SetValue(null,true);conversation.GetMethod("ReleaseActionHold",S).Invoke(null,new object[]{"superdash"});Check((bool)hold.GetValue(null),"Unrelated dash idle cannot cancel held healing");conversation.GetMethod("ReleaseActionHold",S).Invoke(null,new object[]{"focus"});Check(!(bool)hold.GetValue(null),"Native focus finish releases held healing");owns.SetValue(null,false);group.SetValue(null,null);
  Console.WriteLine("Normal/slug healing, ground/wall charge, release/long-flight/room-entry, cancellation and scoped ownership checks passed");

  int heldFrames=0;bool recovered=false;
  for(int visit=0;visit<4;visit++){
   float angle=visit==0?0f:visit==2?-(float)Math.PI*.5f:(float)Math.PI*.5f;
   using(var pose=new OpeningMarioPose(native,library,0f,true)){
    pose.TalkFacing(angle);
    for(int frame=0;frame<240;frame++){
     int expected=visit==0?(frame<210?0x97:0x96):visit==1?(frame<45?0x97:frame<210?0x8C:0x8D):visit==2?(frame<210?0x97:0x96):0x8C;
     if(expected==0x97)pose.LimitAnimationFrame(expected,2);
     int previous=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{previous});}TestEnvironment.PullAudio(audioArgs);
     var t=pose.State.GetType();int af=Convert.ToInt32(t.GetField("animFrame").GetValue(pose.State));Check(Convert.ToInt32(t.GetField("animID").GetValue(pose.State))==expected,"Actual ROM healing/charge/flight/recovery animation");
     if(expected==0x97){Check(af<=2,"Early crouch remains partial throughout sustained healing/charge");if(af==2)heldFrames++;}if(expected==0x96&&af>2)recovered=true;
     Check(Math.Abs(Convert.ToSingle(t.GetField("faceAngle").GetValue(pose.State))-angle)<.002f,"Front-facing healing and both crystal dash directions");Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Actual ROM geometry present throughout flight and holds");
    }
   }
  }
  Check(heldFrames>440&&recovered,"Sustained partial crouch holds beyond five seconds and resumes stand-up animation");
  Console.WriteLine("Actual ROM partial crouch held at frame 2, both charge directions, full slide-kick flight, standing/slide recovery, and safe repeated actor reuse passed");
flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Focus/crystal presentation checks passed without opening a game or save.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
