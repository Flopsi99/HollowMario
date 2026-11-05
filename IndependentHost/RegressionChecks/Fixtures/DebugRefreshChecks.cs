// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
class DebugRefreshChecks {
 static BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static HeroController hero; static int updates; static List<string> events=new List<string>();
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static bool Localize(int __0,ref string __result){__result="Charm "+__0;return false;}
 static bool InSave(ref bool __result){__result=true;return false;}
 static bool Hero(ref HeroController __result){__result=hero;return false;}
 static bool CharmUpdate(){updates++;return false;}
 static bool Broadcast(string __0){events.Add(__0);return false;}
 static int Main(string[] args){try {
 string root=Path.GetFullPath(args[0]),managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 return Run(root);
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static int Run(string root){
 var mod=Assembly.LoadFrom(Path.Combine(root,@"LocalTools\DebugTools\bin\Release\HollowMario.DebugTools.dll"));Type modType=mod.GetType("HollowMarioDebug.DebugTools");
 var data=(PlayerData)FormatterServices.GetUninitializedObject(typeof(PlayerData)); typeof(PlayerData).GetField("_instance",S).SetValue(null,data);
 data.nailSmithUpgrades=0;data.nailDamage=5;data.maxHealthBase=5;data.maxHealth=5;data.health=5;data.equippedCharms=new List<int>();
 hero=(HeroController)FormatterServices.GetUninitializedObject(typeof(HeroController));
 var h=new Harmony("HollowMario.DebugRefreshChecks");
 h.Patch(modType.GetMethod("CharmLabel",S),prefix:new HarmonyMethod(typeof(DebugRefreshChecks),"Localize"));
 h.Patch(modType.GetProperty("InSave",S).GetGetMethod(true),prefix:new HarmonyMethod(typeof(DebugRefreshChecks),"InSave"));
 h.Patch(typeof(HeroController).GetProperty("instance",S).GetGetMethod(true),prefix:new HarmonyMethod(typeof(DebugRefreshChecks),"Hero"));
 h.Patch(typeof(HeroController).GetMethod("CharmUpdate",I),prefix:new HarmonyMethod(typeof(DebugRefreshChecks),"CharmUpdate"));
 h.Patch(typeof(PlayMakerFSM).GetMethod("BroadcastEvent",S,null,new[]{typeof(string)},null),prefix:new HarmonyMethod(typeof(DebugRefreshChecks),"Broadcast"));
 object tool=FormatterServices.GetUninitializedObject(modType);var rows=(IEnumerable)modType.GetMethod("GetMenuData").Invoke(tool,new object[]{null});
 Action<int> nail=null,masks=null;
 foreach(object row in rows){var type=row.GetType();string name=(string)type.GetField("Name").GetValue(row);if(name=="Nail")nail=(Action<int>)type.GetField("Saver").GetValue(row);if(name=="Masks")masks=(Action<int>)type.GetField("Saver").GetValue(row);}
 Check(nail!=null&&masks!=null,"Actual menu savers available");
 for(int level=0;level<=4;level++){int before=updates;nail(level);Check(data.nailSmithUpgrades==level&&data.nailDamage==5+4*level,"Nail menu sets matching damage");Check(updates==before+1&&events.Count==updates&&events[events.Count-1]=="CHARM INDICATOR CHECK","Nail change refreshes native equipment FSMs after data update");}
 for(int level=0;level<=4;level++){int before=updates;masks(level);Check(data.maxHealthBase==5+level&&data.maxHealth==5+level,"Mask menu sets selected capacity");Check(updates==before+1&&events.Count==updates&&events[events.Count-1]=="CHARM INDICATOR CHECK","Mask change refreshes native display");}
 var save=(SaveGameData)FormatterServices.GetUninitializedObject(typeof(SaveGameData));save.playerData=data;modType.GetMethod("BeforeSave",I).Invoke(tool,new object[]{save});Check(save.playerData.nailDamage==5&&save.playerData.maxHealthBase==5&&data.nailDamage==21&&data.maxHealthBase==9,"Save retains original upgrades while live debug upgrades remain");
 int last=updates;modType.GetMethod("Restore",I).Invoke(tool,null);Check(data.nailDamage==5&&data.maxHealthBase==5&&updates==last+1&&events.Count==updates,"Restoring originals also refreshes native equipment and display");
 h.UnpatchSelf();Console.WriteLine("All five nail and mask menu choices notify native equipment FSMs; save protection and restore refresh passed (Unity endpoints intercepted).");return 0;
 }
}
