// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
class DreamerSequenceChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
 string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var seq=mod.GetType("HollowMario.DreamerSequenceMario",true);var h=new Harmony("dreamer-sequence-checks");seq.GetMethod("Arm",S).Invoke(null,new object[]{h,game.GetType("HeroController"),game.GetType("tk2dSpriteAnimator")});
 var play=AccessTools.Method(game.GetType("tk2dSpriteAnimator"),"Play",new[]{game.GetType("tk2dSpriteAnimationClip"),typeof(float),typeof(float)});bool installed=false;foreach(var prefix in Harmony.GetPatchInfo(play).Prefixes)if(prefix.PatchMethod.DeclaringType==seq)installed=true;Check(installed,"Native proxy animation hook installed");
 var phase=seq.GetMethod("Phase",S);Func<string,string,string,int> which=(scene,owner,clip)=>(int)phase.Invoke(null,new object[]{scene,owner,clip});
 Check(which("RestingGrounds_04","Knight Lift","Dreamer Lift")==1,"Dreamer lift matched");
 for(int n=1;n<=3;n++)Check(which("Dream_Nailcollection","Knight Dummy","DN Get "+n)==n+1,"All three pickup phases matched");
 Check(which("Dream_NailCollection","Knight Dummy(Clone)","DN Get 1")==2,"Native scene spelling and cloned proxy supported");
 foreach(string scene in new[]{"Town","Dream_Final_Boss","RestingGrounds_07"})Check(which(scene,"Knight Dummy","DN Get 1")==0&&which(scene,"Knight Lift","Dreamer Lift")==0,"Other scenes not intercepted");
 Check(which("Dream_Nailcollection","Knight","DN Get 1")==0&&which("RestingGrounds_04","Enemy","Dreamer Lift")==0,"Real Hero and unrelated actors untouched");
 var at=seq.GetMethod("At",S);Func<int,float,object> sample=(kind,age)=>at.Invoke(null,new object[]{kind,age});Func<object,string,int> value=(x,name)=>(int)x.GetType().GetField(name,I).GetValue(x);
 for(int frame=0;frame<42;frame++){object p=sample(1,(frame+.1f)/30f);Check(value(p,"Animation")==0xCF&&value(p,"Frame")==frame,"Updraft flip uses exact ROM frame");}
 for(int frame=42;frame<200;frame++){object p=sample(1,(frame+.1f)/30f);Check(value(p,"Animation")==0x15&&value(p,"Frame")==((frame-42)%15),"Updraft float loops without restarting flip");}
 int previous=-1;for(int n=0;n<=60;n++){object p=sample(2,n/30f);int frame=value(p,"Frame");Check(value(p,"Animation")==0x67&&frame>=previous&&frame<=3,"Slow reach is monotonic and stops before full punch");previous=frame;}Check(previous==3,"Reach reaches held frame");
 foreach(int kind in new[]{3,4})foreach(float age in new[]{0f,.1f,1f,5f}){object p=sample(kind,age);Check(value(p,"Animation")==0x67&&value(p,"Frame")==3,"Flash and fade phases hold reaching pose");}
 seq.GetField("phase",S).SetValue(null,4);seq.GetField("age",S).SetValue(null,3f);mod.GetType("HollowMario.ConversationMario").GetMethod("Stop",S).Invoke(null,null);Check((int)seq.GetField("phase",S).GetValue(null)==0&&(float)seq.GetField("age",S).GetValue(null)==0f,"Shared scene/pose cleanup clears cutscene clock");
 h.UnpatchSelf();Console.WriteLine("PASS: native proxy hook and exact scene/actor scope; 42-frame wind flip into looping float; slow reach held at frame 3 through pickup effects; shared cleanup resets state. Live cutscene not tested.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
