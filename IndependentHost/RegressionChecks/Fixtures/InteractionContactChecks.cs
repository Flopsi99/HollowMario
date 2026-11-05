// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
class InteractionContactChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 class OutsideFsm {public string Name="Enemy Attack";public object GameObject {get{throw new Exception("Unrelated detector was inspected");}}}
 class OutsideAction {public object Fsm=new OutsideFsm();}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static int Main(string[] args){try{
  string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var contacts=mod.GetType("HollowMario.InteractionContacts",true);var h=new Harmony("interaction-contact-checks");contacts.GetMethod("Arm",S).Invoke(null,new object[]{h});
  foreach(string target in new[]{"TrackTriggerObjects","HutongGames.PlayMaker.Actions.Trigger2dEvent","HutongGames.PlayMaker.Actions.Trigger2dEventLayer"}){
   var method=AccessTools.Method(game.GetType(target,true),target=="TrackTriggerObjects"?"OnTriggerExit2D":"DoTriggerExit2D");bool found=false;
   foreach(var prefix in Harmony.GetPatchInfo(method).Prefixes)if(prefix.PatchMethod.DeclaringType==contacts)found=true;
   Check(found,"Native contact exit hook missing: "+target);
  }
  Check((bool)contacts.GetMethod("BeforeTrackedExit",S).Invoke(null,new object[]{null,null}),"Absent trackers retain native behavior");
  Check((bool)contacts.GetMethod("BeforeEventExit",S).Invoke(null,new object[]{new OutsideAction(),null}),"Enemy and other noninteraction triggers remain untouched");
  var conversation=mod.GetType("HollowMario.ConversationMario",true);var scope=conversation.GetMethod("KnownInteraction",S);
  foreach(string name in new[]{"npc_control","Shop Region","Inspection","inspect","Stag Bell","Stag Control"})Check((bool)scope.Invoke(null,new object[]{name}),"Interaction scope missing: "+name);
  foreach(string name in new[]{"Control","Hero Death Anim","Dream Nail","damages_enemy","chaser"})Check(!(bool)scope.Invoke(null,new object[]{name}),"Interaction patch leaked into "+name);
  h.UnpatchSelf();Console.WriteLine("PASS: all three native exit hooks install, NPC/shop/station scopes match, unrelated enemy/gameplay triggers keep native behavior. Actual overlap and repeat-dialogue behavior still require an in-game check.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
