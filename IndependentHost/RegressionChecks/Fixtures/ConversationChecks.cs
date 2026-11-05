// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class ConversationChecks {
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
  var scope=conversation.GetMethod("KnownInteraction",S);var facing=conversation.GetMethod("Facing",S);
  Check((bool)scope.Invoke(null,new object[]{"Conversation Control"}),"Native NPC interaction accepted");
  foreach(string other in new[]{"Bench Control","npc_dream_dialogue","Control","Conversation Control Extra"})Check(!(bool)scope.Invoke(null,new object[]{other}),"Other scripted owners excluded");
  var poseForClip=conversation.GetMethod("PoseForClip",S);
  Check((int)poseForClip.Invoke(null,new object[]{"LookUp"})==0x1E&&(int)poseForClip.Invoke(null,new object[]{"LookUpEnd"})==0x1F,"Generic interaction includes opening and closing look poses");
  Check((float)facing.Invoke(null,new object[]{-3f,0f,true})<0,"NPC to left selects left-facing profile");Check((float)facing.Invoke(null,new object[]{3f,0f,false})>0,"NPC to right selects right-facing profile");
  Check((float)facing.Invoke(null,new object[]{0f,0f,false})<0,"Vertically aligned NPC uses native facing");
  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}

  int previousId=-1;
  for(int visit=0;visit<3;visit++){
   float angle=(float)facing.Invoke(null,new object[]{visit%2==0?-3f:3f,0f,true});
   using(var pose=new OpeningMarioPose(native,library,0f,true)){
    int id=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(pose);
    if(previousId>=0)Check(id==previousId,"Repeated conversations reuse the safely parked actor");previousId=id;
    pose.TalkFacing(angle);
    for(int frame=0;frame<140;frame++){
     int expected=frame<120?0x1E:0x1F;
     int before=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{expected});
     try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{before});}
     TestEnvironment.PullAudio(audioArgs);var t=pose.State.GetType();
     Check(Convert.ToInt32(t.GetField("animID").GetValue(pose.State))==expected,"Real ROM geometry uses look-up/closing animation");
     Check(Math.Abs(Convert.ToSingle(t.GetField("faceAngle").GetValue(pose.State))-angle)<.002f,"Native pose faces the speaker");
     Check(Convert.ToUInt32(t.GetField("action").GetValue(pose.State))==0x0000130Au,"Dialogue pose remains stationary instead of sleeping/falling");
     Check(Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b))>0,"Real player-ROM geometry produced");
    }
   }
   Check(TestEnvironment.ParkedActors>0,"Native actor retained safely after conversation");
  }
  flourish.GetMethod("Dispose",S).Invoke(null,null);harmony.GetMethod("UnpatchSelf").Invoke(hooks,null);foreach(var pin in pins)pin.Free();
  Console.WriteLine("Conversation hooks, hero-only scope, both speaker directions, real look-up/closing geometry, stationary pose and three repeated handovers passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
