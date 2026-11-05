// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
class MageRangeChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 class Flag {public string Name="Health Check";}
 class OtherTest {public Flag boolVariable=new Flag();public object Fsm{get{throw new Exception("Unrelated AI was inspected");}}}
 static int Main(string[] args){try{
 string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 Assembly.LoadFrom(Path.Combine(managed,"PlayMaker.dll"));var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var guards=mod.GetType("HollowMario.MageRangeContacts",true);var plugin=mod.GetType("HollowMario.Plugin",true);var h=new Harmony("mage-range-checks");guards.GetMethod("Arm",S).Invoke(null,new object[]{h});
 int hooks=0;foreach(string type in new[]{"Trigger2dEvent","Trigger2dEventLayer"})foreach(string phase in new[]{"Enter","Stay","Exit"}){var m=AccessTools.Method(game.GetType("HutongGames.PlayMaker.Actions."+type),"DoTrigger"+phase+"2D");Check(Harmony.GetPatchInfo(m).Prefixes.Any(p=>p.PatchMethod.DeclaringType==guards),"Actual range callback protected");hooks++;}
 foreach(string name in new[]{"OnEnter","OnUpdate"}){var m=AccessTools.Method(game.GetType("HutongGames.PlayMaker.Actions.BoolTest"),name);Check(Harmony.GetPatchInfo(m).Prefixes.Any(p=>p.PatchMethod.DeclaringType==guards),"Both native LOST HERO test paths guarded");hooks++;}
 var range=guards.GetMethod("IsRange",S);foreach(string n in new[]{"Roam Range","Firing Range"}){Check((bool)range.Invoke(null,new object[]{n,"Mage"}),"Mage spatial detector matched");Check(!(bool)range.Invoke(null,new object[]{n,"Mage Lord"}),"Boss detector not intercepted");}Check(!(bool)range.Invoke(null,new object[]{"Shop Region","Mage"}),"Interaction region not intercepted");
 var locked=guards.GetMethod("LockedFight",S);Func<string,string,bool,bool,bool,bool,bool> lockFor=(scene,state,manual,enemy,hero,mario)=>(bool)locked.Invoke(null,new object[]{scene,state,manual,enemy,hero,mario});
 Check(lockFor("Ruins1_09","Started",true,true,true,true),"Live sealed mandatory fight protected");foreach(string state in new[]{"Init","Detect","Enemy Killed","Open"})Check(!lockFor("Ruins1_09",state,true,true,true,true),"Opening/finished/unstarted battle keeps native behavior");
 Check(!lockFor("Ruins1_30","Started",true,true,true,true)&&!lockFor("Ruins1_09","Started",false,true,true,true)&&!lockFor("Ruins1_09","Started",true,false,true,true)&&!lockFor("Ruins1_09","Started",true,true,false,true)&&!lockFor("Ruins1_09","Started",true,true,true,false),"Other rooms, normal roaming, death and Knight-only play keep native behavior");
 Check((bool)guards.GetMethod("BeforeRangeTest",S).Invoke(null,new object[]{new OtherTest()}),"Unrelated Boolean tests exit before inspecting enemy state");
 object current=FormatterServices.GetUninitializedObject(typeof(BoxCollider2D)),retired=FormatterServices.GetUninitializedObject(typeof(BoxCollider2D)),other=FormatterServices.GetUninitializedObject(typeof(BoxCollider2D));plugin.GetField("feetPad",S).SetValue(null,current);plugin.GetField("retiredFeetPad",S).SetValue(null,retired);var owns=plugin.GetMethod("IsFeetPad",S);Check((bool)owns.Invoke(null,new[]{current})&&(bool)owns.Invoke(null,new[]{retired})&&!(bool)owns.Invoke(null,new[]{other})&&!(bool)owns.Invoke(null,new object[]{null}),"Owned live and retired sensor identity survives queued exit callbacks without swallowing another collider");plugin.GetField("feetPad",S).SetValue(null,null);Check((bool)owns.Invoke(null,new[]{retired}),"Retired pad remains recognizable after active pad removal");plugin.GetField("retiredFeetPad",S).SetValue(null,null);
 h.UnpatchSelf();Console.WriteLine("PASS: "+hooks+" native callbacks; Mage-only range scope; mandatory live arena guard with death/gate/room/mode exclusions; unrelated AI untouched; live/retired sensor identity. Actual battle still needs gameplay verification.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
