// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
class BirthplaceChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 class FakeFsm {public string Name="Enemy Control";public object GameObject{get{throw new Exception("Unrelated FSM inspected");}}}
 class FakeState {public object Fsm=new FakeFsm();}
 static int Main(string[] args){try{
 string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 Assembly.LoadFrom(Path.Combine(managed,"PlayMaker.dll"));var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var body=mod.GetType("HollowMario.BirthplaceMario",true);var reflection=mod.GetType("HollowMario.MarioReflection",true);var h=new Harmony("birthplace-checks");body.GetMethod("Arm",S).Invoke(null,new object[]{h,game.GetType("HeroController"),game.GetType("tk2dSpriteAnimator")});reflection.GetMethod("Arm",S).Invoke(null,new object[]{h,engine.GetType("MarioHollow.MarioController")});
 var ctor=AccessTools.Constructor(engine.GetType("MarioHollow.MarioRenderer"),new[]{typeof(string)});bool hooked=false;foreach(var p in Harmony.GetPatchInfo(ctor).Postfixes)if(p.PatchMethod.DeclaringType==reflection)hooked=true;Check(hooked,"All playable/private Mario renderers receive reflection layer hook");
 var phase=body.GetMethod("Phase",S);Func<string,string,string,int> stage=(scene,owner,state)=>(int)phase.Invoke(null,new object[]{scene,"Control",owner,state});
 Check(stage("Abyss_15","Dream Enter Abyss","Impact")==10&&stage("Abyss_15","Dream Enter Abyss","Black")==11,"Egg collapse and prone hold matched");
 foreach(string s in new[]{"Init","Fade Up","Corpse 1","Input","Input 2","Input 3"})Check(stage("Dream_Abyss","Entry Cutscene",s)==20,"Buried opening kept covered including input waits");
 foreach(string s in new[]{"First Shake","Shake 2","Shake 3"})Check(stage("Dream_Abyss","Entry Cutscene",s)==21,"Native struggle input steps preserved");
 Check(stage("Dream_Abyss","Entry Cutscene","Climb 1")==22&&stage("Dream_Abyss","Entry Cutscene","Input 4")==26&&stage("Dream_Abyss","Entry Cutscene","Climb 2")==23&&stage("Dream_Abyss","Entry Cutscene","Input 5")==27&&stage("Dream_Abyss","Entry Cutscene","Climb 3")==24&&stage("Dream_Abyss","Entry Cutscene","End")==-1,"Emergence and explicit return-control boundary matched");
 Check(stage("Dream_Abyss","End Cutscene","Start")==40&&stage("Dream_Abyss","End Cutscene","Clamber1")==41&&stage("Dream_Abyss","End Cutscene","Fall")==42,"Player hanging attempt and foreground falling actor phases matched");
 foreach(string owner in new[]{"King","Hollow Knight","Abyss Drop Corpse","Enemy"})Check(stage("Dream_Abyss",owner,"Climb 1")==0,"Other story actors untouched");Check(stage("Town","Entry Cutscene","Init")==0,"Other scenes untouched");body.GetMethod("BeforeState",S).Invoke(null,new object[]{new FakeState()});
 var at=body.GetMethod("At",S);Func<int,float,object> sample=(kind,p)=>at.Invoke(null,new object[]{kind,p});Func<object,string,int> v=(p,name)=>(int)p.GetType().GetField(name,I).GetValue(p);
 for(int n=0;n<=100;n++){float p=n/100f;object c=sample(10,p);Check(v(c,"Animation")==0xC5||(v(c,"Animation")==0x2C&&v(c,"Frame")>=0&&v(c,"Frame")<=20),"Collapse never runs belly recovery");object climb=sample(41,p);Check((v(climb,"Animation")==0&&v(climb,"Frame")<=7)||v(climb,"Animation")==0x33,"Climb remains below one third of 34-frame animation");}
 Check(v(sample(10,1f),"Frame")==20&&v(sample(11,1f),"Frame")==20,"Passed-out pose remains prone");Check(v(sample(41,.5f),"Frame")==7&&v(sample(41,.75f),"Frame")<7&&v(sample(41,1f),"Animation")==0x33,"Failed climb reverses to hanging");Check(v(sample(24,0f),"Frame")==116&&v(sample(24,1f),"Frame")==150,"Get-out section follows actual SM64 unstuck frames");Check(v(sample(42,.9f),"Animation")==2,"Fall uses backward airborne pose");
 body.GetField("phase",S).SetValue(null,42);mod.GetType("HollowMario.ConversationMario").GetMethod("Stop",S).Invoke(null,null);Check((int)body.GetField("phase",S).GetValue(null)==0,"Scene cleanup clears presentation ownership");h.UnpatchSelf();Console.WriteLine("PASS: reflection constructor hook; exact scene/controller scopes excluding King/Hollow Knight/corpses; native input waits, emergence and control-return mapping; prone hold, seven-frame forward/reverse ledge attempt, backward fall and scene cleanup. Live visuals not tested.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
