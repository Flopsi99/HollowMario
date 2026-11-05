// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
class DebugToolsChecks {
 static BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static bool Localize(int __0,ref string __result){__result="Charm "+__0;return false;}
 static bool OutsideGame(ref bool __result){__result=false;return false;}
 static int Main(string[] args){try {
 string root=Path.GetFullPath(args[0]),managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var mod=Assembly.LoadFrom(Path.Combine(root,@"LocalTools\DebugTools\bin\Release\HollowMario.DebugTools.dll"));var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));Type dataType=game.GetType("PlayerData"),sessionType=mod.GetType("HollowMarioDebug.DebugSession"),modType=mod.GetType("HollowMarioDebug.DebugTools");
 object data=FormatterServices.GetUninitializedObject(dataType);dataType.GetField("_instance",S).SetValue(null,data);
 Action<string,object> put=(key,value)=>{var f=dataType.GetField(key,I);Check(f!=null,"Actual game field exists: "+key);f.SetValue(data,value);};Func<string,object> get=key=>dataType.GetField(key,I).GetValue(data);
 put("hasDash",true);put("hasWalljump",true);put("fireballLevel",2);put("quakeLevel",2);put("screamLevel",2);put("hasSpell",true);put("nailSmithUpgrades",4);put("nailDamage",21);put("geo",123);put("charmSlots",3);put("equippedCharms",new List<int>());
 for(int id=1;id<=40;id++)put("charmCost_"+id,2);put("brokenCharm_23",true);
 object session=Activator.CreateInstance(sessionType,I,null,new[]{data},null);Action<string,object[]> call=(name,values)=>Array.Find(sessionType.GetMethods(I),m=>m.Name==name&&m.GetParameters().Length==values.Length).Invoke(session,values);
 call("Toggle",new object[]{"hasDash",0});Check(!(bool)get("hasDash")&&!(bool)get("canDash")&&!(bool)get("hasShadowDash"),"Removing long jump clears its permission and shadow upgrade");call("Toggle",new object[]{"hasDash",1});Check((bool)get("hasDash")&&(bool)get("canDash"),"Long jump grants the native permission used by aerial long jump");
 call("Toggle",new object[]{"hasDash",0});call("Toggle",new object[]{"hasShadowDash",1});Check((bool)get("hasDash")&&(bool)get("canDash")&&(bool)get("hasShadowDash"),"Shade upgrade also grants functional base dash");
 call("Toggle",new object[]{"hasWalljump",1});Check((bool)get("hasWalljump")&&(bool)get("canWallJump"),"Wall Jump matches both actual pickup flags");call("Toggle",new object[]{"hasWalljump",0});Check(!(bool)get("canWallJump"),"Wall Jump permission clears on removal");
 call("Toggle",new object[]{"hasDreamGate",1});Check((bool)get("hasDreamNail")&&(bool)get("hasDreamGate"),"Dreamgate grants its Dream Nail prerequisite");
 for(int form=0;form<=2;form++){call("Royal",new object[]{form});Check((int)get("royalCharmState")==form&&!(bool)get("gotCharm_36")&&!(bool)get("equippedCharm_36"),"Royal fragment remains an incomplete inventory item");}
 call("Royal",new object[]{3});Check((bool)get("gotCharm_36")&&!(bool)get("equippedCharm_36")&&(int)get("charmCost_36")==5,"Whole Kingsoul is owned but not automatically equipped");call("Charm",new object[]{36,2});call("Royal",new object[]{4});Check((bool)get("equippedCharm_36")&&(int)get("charmCost_36")==0,"Void Heart is auto-equipped with zero notches");call("Charm",new object[]{36,0});Check((int)get("royalCharmState")==0&&!(bool)get("gotCharm_36"),"Removing special charm clears its form selector");
 call("Charm",new object[]{36,1});Check((int)get("royalCharmState")==3,"Owning Kingsoul supplies the complete form");call("Charm",new object[]{36,0});
 call("Put",new object[]{"grimmChildLevel",5});call("Charm",new object[]{40,2});Check((int)get("charmCost_40")==3,"Carefree Melody uses its own notch cost");call("Charm",new object[]{40,0});Check((int)get("grimmChildLevel")==0,"Removing Grimmchild clears the form selector");call("Charm",new object[]{40,1});Check((int)get("grimmChildLevel")==1&&(int)get("charmCost_40")==2,"Regranting Grimmchild creates a valid base form");call("Charm",new object[]{40,0});
 call("Spell",new object[]{"fireballLevel",1});Check((int)get("fireballLevel")==1&&(bool)get("hasSpell"),"Normal spell can replace upgraded spell");
 foreach(string spell in new[]{"fireballLevel","quakeLevel","screamLevel"})call("Spell",new object[]{spell,0});Check(!(bool)get("hasSpell"),"No spells updates aggregate flag");
 for(int level=0;level<=4;level++){call("Nail",new object[]{level});Check((int)get("nailDamage")==5+4*level,"Nail stage and damage agree");}
 call("Put",new object[]{"hasCyclone",1});call("Put",new object[]{"hasUpwardSlash",1});call("Put",new object[]{"hasDashSlash",1});call("Arts",new object[0]);Check((bool)get("hasNailArt")&&(bool)get("hasAllNailArts"),"Cap unlock aggregates");
 call("Put",new object[]{"hasDreamGate",1});call("Dream",new object[]{0});Check(!(bool)get("hasDreamNail")&&!(bool)get("hasDreamGate"),"Removing Dream Nail disables gate");call("Dream",new object[]{2});Check((bool)get("dreamNailUpgraded"),"Awoken level");
 call("Charm",new object[]{23,2});Check((bool)get("gotCharm_23")&&(bool)get("equippedCharm_23")&&!(bool)get("brokenCharm_23"),"Charm equips and repairs for testing");Check(((List<int>)get("equippedCharms")).Contains(23)&&(int)get("charmSlotsFilled")==2,"Equipped list and costs agree");
 call("Charm",new object[]{23,0});Check(!(bool)get("equippedCharm_23")&&!((List<int>)get("equippedCharms")).Contains(23),"Removing charm also unequips it");
 call("Refresh",new object[]{new Action(()=>put("maxHealth",7))});
 put("geo",456);object copied=sessionType.GetMethod("ForSave",I).Invoke(session,new object[]{null});Check(!ReferenceEquals(copied,data),"Save is a separate PlayerData copy");Check((int)dataType.GetField("fireballLevel").GetValue(copied)==2&&(int)get("fireballLevel")==0,"Saved original upgrades; live debug selection remains");Check(!(bool)dataType.GetField("canDash").GetValue(copied)&&(bool)get("canDash"),"Save excludes temporary dependent dash permission while live gameplay retains it");Check((int)dataType.GetField("geo").GetValue(copied)==456,"Unrelated progress retained");Check((int)dataType.GetField("maxHealth").GetValue(copied)==0,"Native refresh derived changes excluded from saved overrides");Check((bool)dataType.GetField("brokenCharm_23").GetValue(copied),"Original fragile-charm state restored in save");
 var h=new Harmony("HollowMario.DebugToolsChecks");h.Patch(modType.GetMethod("CharmLabel",S),prefix:new HarmonyMethod(typeof(DebugToolsChecks),"Localize"));h.Patch(modType.GetProperty("InSave",S).GetGetMethod(true),prefix:new HarmonyMethod(typeof(DebugToolsChecks),"OutsideGame"));
 object tool=FormatterServices.GetUninitializedObject(modType);var rows=(IEnumerable)modType.GetMethod("GetMenuData").Invoke(tool,new object[]{null});int count=0;foreach(object row in rows){var type=row.GetType();Check(!Convert.ToString(type.GetField("Name").GetValue(row)).ToLowerInvariant().Contains("filming"),"Menu names use general debug wording");((Func<int>)type.GetField("Loader").GetValue(row))();count++;}Check(count>=65,"Menu includes upgrade stages and all forty charms");
 modType.GetField("session",I).SetValue(tool,session);object save=FormatterServices.GetUninitializedObject(game.GetType("SaveGameData"));game.GetType("SaveGameData").GetField("playerData").SetValue(save,data);modType.GetMethod("BeforeSave",I).Invoke(tool,new[]{save});object saved=game.GetType("SaveGameData").GetField("playerData").GetValue(save);Check(!ReferenceEquals(saved,data)&&(int)dataType.GetField("fireballLevel").GetValue(saved)==2&&(int)get("fireballLevel")==0,"Actual save callback protects upgrades without changing live gameplay");
 modType.GetMethod("Restore",I).Invoke(tool,null);Check((int)get("fireballLevel")==2&&(int)get("geo")==456&&((List<int>)get("equippedCharms")).Count==0,"Disabling utility restores upgrades and keeps unrelated progress");
 h.UnpatchSelf();Console.WriteLine("Actual PlayerData fields, "+count+" menu loaders, spell/nail/cap/Dream Nail/charm consistency, cloned-save protection, native-refresh tracking and unload restoration passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
